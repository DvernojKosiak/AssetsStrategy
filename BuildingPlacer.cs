using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

[System.Serializable]
public class BuildingType
{
    public string name = "Здание";
    public Vector2Int size = new Vector2Int(2, 2);

    [Header("Стоимость постройки")]
    public int costGold = 0;
    public int costWood = 0;
    public int costStone = 0;
    public int costFood = 0;

    [Header("Пассивное производство (в секунду)")]
    public int prodGold = 0;
    public int prodWood = 0;
    public int prodStone = 0;
    public int prodFood = 0;

    [Header("Лимит хранилища (на 1 ур.)")]
    public int maxStorage = 50;

    public Color color = Color.white;
    public GameObject prefab;
}

public class BuildingPlacer : MonoBehaviour
{
    [Header("Префаб-заготовка")]
    public GameObject buildingPrefab;

    [Header("Префаб Цитадели")]
    public GameObject townHallCustomPrefab;

    [Header("Префаб Шахты")]
    public GameObject mineCustomPrefab;

    [Header("Префаб Дороги")]
    public GameObject roadCustomPrefab;

    [Header("Префаб Фермы")]
    public GameObject farmCustomPrefab;

    [Header("Префаб Лесопилки")]
    public GameObject sawmillCustomPrefab;

    [Header("Префаб Жилого дома")]
    public GameObject houseCustomPrefab;

    [Header("Префаб Мастерской")]
    [UnityEngine.Serialization.FormerlySerializedAs("WorkshopPrefab")]
    [UnityEngine.Serialization.FormerlySerializedAs("workshopPrefab")]
    public GameObject workshopCustomPrefab;

    [Header("Префаб Башни")]
    [UnityEngine.Serialization.FormerlySerializedAs("TowerPrefab")]
    [UnityEngine.Serialization.FormerlySerializedAs("towerPrefab")]
    public GameObject towerCustomPrefab;

    [Header("Типы зданий")]
    public BuildingType[] buildingTypes;

    [Header("Карта")]
    public int mapSize = 50;

    [Header("Ягодные кусты (для Станции собирателей)")]
    public int berryBushes = 40;

    [Header("Цвета")]
    public Color backColor = new Color(0.11f, 0.14f, 0.20f);
    public Color lineColor = new Color(0.22f, 0.28f, 0.40f);
    public Color okColor = new Color(0.4f, 1f, 0.4f, 0.6f);
    public Color badColor = new Color(1f, 0.3f, 0.3f, 0.6f);

    [Header("Трава")]
    public Color grassDark = new Color(0.20f, 0.42f, 0.16f);
    public Color grassLight = new Color(0.36f, 0.62f, 0.25f);
    public bool showGridLines = true;      // еле заметная сетка, чтобы было видно клетки
    [Range(0f, 0.3f)] public float gridLineStrength = 0.10f;

    // ---------- Окно построек ----------
    private bool shopOpen = false;
    private Rect shopRect;
    private float shopScroll = 0f;      // прокрутка окна построек (колёсико или полоса справа)
    private float shopMaxScroll = 0f;
    private readonly Rect buildButtonRect = new Rect(10, 45, 240, 40);
    /// <summary>klan-voina-14: где кнопки слева сверху ловят нажатия (в военной деревне там только «?»).</summary>
    Rect MenuRect => WarCityMode.Active ? new Rect(buildButtonRect.x, buildButtonRect.y, 40f, buildButtonRect.height) : buildButtonRect;
    private bool placeSpaceOk = true;   // для подсказки у курсора: есть ли место под зданием
    private int placeDoorProblem = 0;   // 0 - ок, 1 - у самого здания нет свободной стороны для двери, 2 - закроет выход соседу
    private bool placeZoneOk = true;    // ... и входит ли оно в зелёную зону
    private string placeWaterProblem = "";   // ... и не мешает ли река (обычное здание на воде / мост не на воде)
    private bool placeGuard = false;    // клик, которым выбрали постройку, не должен сразу ставить её "сквозь" окно
    private bool wasWar = false;        // для сообщения о начале войны

    // Описание в два слова для карточки
    static readonly Dictionary<string, string> ShortInfo = new Dictionary<string, string>
    {
        { "Стена",      "Задерживает врагов" },
        { "Жилой дом",  "Жильё для горожан" },
        { "Лесопилка",  "Дерево; Ур.3 — смола" },
        { "Шахта",      "Камень, золото; Ур.3 — руда" },
        { "Ферма",      "Еда; Ур.3 — овёс; Ур.4 — больше еды и овса" },
        { "Мастерская", "Доски и плиты" },
        { BarracksController.TypeName, "Обучает солдат" },
        { "Башня",      "Стреляет по врагам" },
        { "Дорожка",    "Ходить быстрее" },
        { BuildingInstance.CobbleName, "Быстро и пешим, и конным" },
        { HealerHouse.TypeName, "Лечат и кормят" },
        { BuildingInstance.GateName, "Проход в стене" },
        { MagicCannonController.TypeName, "Взрывает врагов по области" },
        { CourierHouse.TypeName, "Курьеры сами носят добычу" },
        { WarehouseController.TypeName, "Доски, плиты и сталь" },
        { ArmoryController.TypeName, "Мечи, луки и щиты" },
        { StableController.TypeName, "Лошади из овса" },
        { OutpostController.TypeName, "Расширяет зону; нужен подвоз еды" },
        { BerryStation.TypeName, "Ягоды с кустов за городом" },
        { KitchenController.TypeName, "Похлёбка для войск" },
        { WagonYard.TypeName, "Обозы в Дальний склад, враги не достанут" },
        { PotionHouse.TypeName, "Магия: зелье отравления из трав" },
        { WeaponForge.TypeName, "Война: доводка оружия (мини-игра)" },
        { FoundryController.TypeName, "Инженерия: детали из руды и стали" },
        { EngineerYard.TypeName, "Инженерия: модули для башен и ловушки" },
        { MarketHouse.TypeName, "Рынок: торговля за марки" },
        { BuildingInstance.BridgeName, "Переход через реку (на воду)" },
        { BuildingInstance.GuestHouseName, "Жильё только для наёмников" },
        { GroundCannonController.TypeName, "Ядра прямой наводкой, осколки" },
        { MagicArmory.TypeName, "Магия: снаряды для пушек" },
        { LifeAltar.TypeName, "Магия жизни: растит голема" },
        { EngineerBarracks.TypeName, "Военный инженер: катапульты" },
        { IndustrialPlant.TypeName, "Индустрия: ускоряет производства рядом" },
        { WarBuilderYard.TypeName, "Военный строитель: чинят здания в бою" },
    };

    // С какого уровня Цитадели можно строить (1 - сразу).
    // Цитадель Ур.1 - основа города: жильё, добыча, мастерская, казарма, башни, стены, лекари, Хранилище, собиратели, Курьерская.
    // Цитадель Ур.2 - снабжение и торговля: Оружейная, Конюшня, Кухня, Аванпост, брусчатка, мост,
    //               Торговый дом, Гостевой дом, Магическая пушка (и здания пути - путь выбирают тоже на Ур.2).
    // Цитадель Ур.3 - Обозный двор (Дальний склад), Пушка, здания школ и направлений.
    static int HallLevelNeeded(string typeName)
    {
        if (typeName == BuildingInstance.BridgeName) return 2;   // мост - с Цитадели Ур.2
        if (typeName == ArmoryController.TypeName || typeName == StableController.TypeName || typeName == KitchenController.TypeName
            || typeName == OutpostController.TypeName || typeName == BuildingInstance.CobbleName
            || typeName == MarketHouse.TypeName || typeName == BuildingInstance.GuestHouseName || typeName == MagicCannonController.TypeName)
            return 2;
        if (typeName == WagonYard.TypeName) return 3;            // Дальний склад - поздняя игра (ячейки от 1500 золота)
        if (typeName == GroundCannonController.TypeName) return GroundCannonController.RequiredHallLevel;   // пушка - с Цитадели Ур.3
        if (typeName == MagicArmory.TypeName) return MagicArmory.RequiredHallLevel;
        if (typeName == LifeAltar.TypeName) return LifeAltar.RequiredHallLevel;
        if (typeName == EngineerBarracks.TypeName || typeName == IndustrialPlant.TypeName) return 3;   // направления инженеров - с Цитадели Ур.3
        if (typeName == WarBuilderYard.TypeName) return WarBuilderYard.RequiredHallLevel;              // ветки военного дела - с Цитадели Ур.3
        return 1;
    }

    /// <summary>Уровень Цитадели сейчас (1, если её ещё нет).</summary>
    public int HallLevelNow()
    {
        BuildingInstance hall = PopulationManager.Instance != null ? PopulationManager.Instance.Hall : null;
        int lvl = hall != null ? hall.level : 1;
        foreach (var inst in placedInstances)
            if (inst != null && inst.IsTownHall) lvl = Mathf.Max(lvl, inst.level);
        return Mathf.Max(1, lvl);
    }

    // Обновление 13: здание, которому без другого здания нечего делать, - сначала то здание.
    // Кухне нужен овёс, а его даёт только Ферма Ур.3 (раньше Кухню можно было поставить и не пользоваться ею)
    string NeedProblem(BuildingType t)
    {
        if (t == null) return "";
        if (t.name == KitchenController.TypeName && CountOfType(t.name) == 0)
        {
            foreach (var b in placedInstances)
                if (b != null && b.ProducesOats && !b.IsRuined) return "";
            return $"Нужна Ферма Ур.{BuildingInstance.OatsFromLevel}: похлёбку варят из овса, а его даёт только она";
        }
        return "";
    }

    bool HallLevelOk(BuildingType t)
    {
        int need = t != null ? HallLevelNeeded(t.name) : 1;
        if (need <= 1) return true;
        return HallLevelNow() >= need;
    }

    // Сколько зданий этого типа можно поставить при Цитадели Ур.1 / 2 / 3 (нет в таблице - сколько угодно).
    // Обновление 12: казарм и обороны было слишком много (7 казарм и 12 башен на Ур.3 - армия «без счёта»,
    // и набеги по такой деревне тормозили). Уже стоящие здания сверх лимита не сносятся, только новые не ставятся.
    static readonly Dictionary<string, int[]> CountByHall = new Dictionary<string, int[]>
    {
        { BarracksController.TypeName,      new[] { 2, 3, 5 } },   // было 3 / 5 / 7
        { "Башня",                          new[] { 3, 6, 9 } },   // было 4 / 8 / 12
        { MagicCannonController.TypeName,   new[] { 0, 1, 3 } },   // с Цитадели Ур.2; раньше без счёта
        { GroundCannonController.TypeName,  new[] { 0, 0, 2 } },   // с Цитадели Ур.3; раньше без счёта
        { WarehouseController.TypeName,     new[] { 1, 2, 3 } },
        { OutpostController.TypeName,       new[] { 0, 1, 3 } },   // было 0 / 2 / 4
    };

    // Вся оборона вместе (башни, пушки, магические пушки, аванпосты): не больше стольких на Цитадель Ур.1 / 2 / 3
    static readonly int[] DefenseByHall = { 3, 7, 12 };

    static bool IsDefenseType(string n)
    {
        return n == "Башня" || n == MagicCannonController.TypeName || n == GroundCannonController.TypeName || n == OutpostController.TypeName;
    }

    int DefenseCount()
    {
        int n = 0;
        foreach (var inst in placedInstances)
            if (inst != null && inst.type != null && IsDefenseType(inst.type.name)) n++;
        return n;
    }

    int DefenseMax() { return DefenseByHall[Mathf.Clamp(HallLevelNow(), 1, 3) - 1]; }

    // Сколько зданий этого типа можно поставить (0 - сколько угодно)
    int MaxOfType(string typeName)
    {
        if (typeName == MarketHouse.TypeName) return 1;   // Торговый дом - один на город
        if (typeName == LifeAltar.TypeName) return 1;     // Алтарь жизни - один (и голем один)
        if (typeName == EngineerBarracks.TypeName) return 1;   // Инженерная казарма - одна
        if (typeName == IndustrialPlant.TypeName) return 1;    // Завод - один
        if (typeName == WarBuilderYard.TypeName) return 1;     // Двор военного строителя - один
        if (typeName != null && CountByHall.TryGetValue(typeName, out int[] byHall))
            return Mathf.Max(1, byHall[Mathf.Clamp(HallLevelNow(), 1, 3) - 1]);
        return 0;
    }

    bool AtLimit(BuildingType t)
    {
        if (t == null) return false;
        if (IsDefenseType(t.name) && DefenseCount() >= DefenseMax()) return true;
        int max = MaxOfType(t.name);
        return max > 0 && CountOfType(t.name) >= max;
    }

    // Почему больше не построить: «только один» или «не больше N при Цитадели Ур.X (на Ур.X+1 - M)»
    string LimitText(BuildingType t)
    {
        int lvl = Mathf.Clamp(HallLevelNow(), 1, 3);
        if (t != null && IsDefenseType(t.name) && DefenseCount() >= DefenseMax())
        {
            string nx = lvl < 3 ? $", на Ур.{lvl + 1} — {DefenseByHall[lvl]}" : "";
            return $"Обороны (башни, пушки, аванпосты) не больше {DefenseMax()} при Цитадели Ур.{lvl}{nx}";
        }
        if (t == null || !CountByHall.TryGetValue(t.name, out int[] byHall)) return "Можно построить только один";
        int max = MaxOfType(t.name);
        string next = lvl < 3 ? $", на Ур.{lvl + 1} — {byHall[lvl]}" : "";
        return $"Не больше {max} при Цитадели Ур.{lvl}{next}";
    }

    /// <summary>Подпись «построено N из M» для карточки здания в окне построек ("" - без лимита).</summary>
    string CountChip(BuildingType t)
    {
        if (t == null) return "";
        int max = MaxOfType(t.name);
        if (max <= 0 || max == 1 && !CountByHall.ContainsKey(t.name)) return "";
        return $"{CountOfType(t.name)}/{max}";
    }

    // Здания путей развития: в окне построек видны только выбравшему этот путь
    static PathKind PathOf(string typeName)
    {
        if (typeName == PotionHouse.TypeName || typeName == MagicArmory.TypeName || typeName == LifeAltar.TypeName) return PathKind.Magic;
        if (typeName == WeaponForge.TypeName || typeName == WarBuilderYard.TypeName) return PathKind.War;
        if (typeName == FoundryController.TypeName || typeName == EngineerYard.TypeName
            || typeName == EngineerBarracks.TypeName || typeName == IndustrialPlant.TypeName) return PathKind.Engineering;
        return PathKind.None;
    }

    // Здания школ магии (внутри пути Магии): видны только выбравшему школу
    static MagicSchool SchoolOf(string typeName)
    {
        if (typeName == LifeAltar.TypeName) return MagicSchool.Life;
        return MagicSchool.None;
    }

    // Здания направлений инженеров (внутри пути Инженерии): видны только выбравшему направление
    static EngSchool EngSchoolOf(string typeName)
    {
        if (typeName == EngineerBarracks.TypeName) return EngSchool.Military;
        if (typeName == IndustrialPlant.TypeName) return EngSchool.Industrial;
        return EngSchool.None;
    }

    // Здания веток военного дела (внутри пути Военного дела): видны только выбравшему ветку
    static WarSchool WarSchoolOf(string typeName)
    {
        if (typeName == WarBuilderYard.TypeName) return WarSchool.Builder;
        return WarSchool.None;
    }

    /// <summary>Здание доступно выбранному пути, школе магии, направлению инженеров и ветке военного дела.</summary>
    static bool PathAllows(string typeName)
    {
        return DevelopmentPath.Allows(PathOf(typeName)) && DevelopmentPath.SchoolAllows(SchoolOf(typeName))
            && DevelopmentPath.EngAllows(EngSchoolOf(typeName)) && DevelopmentPath.WarAllows(WarSchoolOf(typeName));
    }

    private BuildingType townHall;
    private int selected = 0;
    private bool isBuildingMode = false;

    private Dictionary<Vector2Int, GameObject> occupied = new Dictionary<Vector2Int, GameObject>();
    // Стартовые постройки (лесопилка, шахта, ферма, жилой дом из начала игры) - их нельзя снести
    private readonly HashSet<GameObject> starterBuildings = new HashSet<GameObject>();
    private List<BuildingInstance> placedInstances = new List<BuildingInstance>();
    private BuildingInstance selectedInstance = null;
    private CampMarker selectedCamp = null;   // выбранный полевой лагерь отряда
    private EnemyAgent selectedEnemy = null;     // выбранный враг (карточка только для просмотра)
    private LifeGolem selectedGolem = null;      // выбранный голем Алтаря жизни (финал: только карточка, приказов нет)
    private CitizenAgent selectedCitizen = null; // выбранный житель (карточка только для просмотра)
    private Rect infoPanelRect = new Rect(265, 45, 315, 335); // реальный размер панели выбранного здания

    private Camera cam;
    private GameObject ghost;
    private Sprite ghostPlainSprite;   // спрайт призрака из префаба (для зданий без картинки)

    private bool isMoving = false;
    private GameObject movingBuilding = null;
    private BuildingType movingBuildingType = null;

    BuildingType Current => buildingTypes[selected];

    public static BuildingPlacer Instance { get; private set; }

    /// <summary>Обновление 12 (избранные жители): выбрать жителя, открыть карточку и вести камеру за ним.</summary>
    public static void FollowCitizen(CitizenAgent a)
    {
        var b = Instance;
        if (b == null || a == null || a.IsDead) return;
        if (GameMenu.Instance != null && GameMenu.Instance.IsOpen && UiKit.Phone) GameMenu.Instance.Toggle();   // на телефоне меню закрыло бы карту
        b.selectedCitizen = a;
        b.selectedEnemy = null;
        b.selectedGolem = null;
        b.selectedInstance = null;
        b.selectedCamp = null;
        CitizenInfoPanel.OrderArmed = false;
        CitizenInfoPanel.Follow = true;
        // final-13-fix-2: сидит в здании - камера на это здание (над ним звёздочка избранного)
        BuildingInstance inside = a.InsideBuilding;
        if (CameraController.Instance != null) CameraController.Instance.FocusOn(inside != null ? inside.Center : (Vector2)a.transform.position);
    }

    /// <summary>Точка окна (точки окон) над панелью выбранного здания / жителя / врага, её кнопками или окном построек.</summary>
    /// <summary>Под точкой экрана (пиксели, как у мыши) стоит здание. Долгое нажатие на пустую траву меню не открывает.</summary>
    public static bool HasBuildingAtScreen(Vector2 screen)
    {
        var me = Instance;
        if (me == null || me.cam == null) return false;
        Vector3 w = me.cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, Mathf.Abs(me.cam.transform.position.z)));
        return me.occupied.TryGetValue(new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.y)), out GameObject b) && b != null;
    }

    /// <summary>Что можно сделать со зданием под пальцем: 1 - перенести, 2 - снести (биты). 0 - ничего
    /// (трава, Цитадель, мост без сноса) - тогда меню по долгому нажатию не открываем.</summary>
    public static int ActionsAtScreen(Vector2 screen)
    {
        var me = Instance;
        if (me == null || me.cam == null) return 0;
        Vector3 w = me.cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, Mathf.Abs(me.cam.transform.position.z)));
        if (!me.occupied.TryGetValue(new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.y)), out GameObject go) || go == null) return 0;
        var b = go.GetComponent<BuildingInstance>();
        if (b == null) return 0;
        int f = 0;
        if (!b.IsTownHall && !b.IsBridge) f |= 1;
        if (go.name != (me.townHall != null ? me.townHall.name : "") && !b.IsTownHall && !me.starterBuildings.Contains(go)) f |= 2;
        return f;
    }

    bool leftHidden;   // кнопки слева сверху спрятаны под чужим окном

    // Чужое окно закрывает прямоугольник (проверяем углы и середину)
    public static bool OtherWindowOver(Rect r)
    {
        Vector2[] pts = { new Vector2(r.x + 4f, r.y + 4f), new Vector2(r.xMax - 4f, r.y + 4f), r.center,
                          new Vector2(r.x + 4f, r.yMax - 4f), new Vector2(r.xMax - 4f, r.yMax - 4f) };
        try
        {
            foreach (var p in pts)
                if (RaidManager.IsPointerOverUI(p) || DevelopmentPath.IsPointerOver(p) || WeaponForge.IsPointerOver(p)
                    || MarketWindow.IsPointerOver(p) || ClanWindow.IsPointerOver(p) || WarView.IsPointerOver(p)
                    || (GameMenu.Instance != null && GameMenu.Instance.IsPointerOver(p))) return true;
        }
        catch (System.Exception) { }
        return false;
    }

    public static bool IsPointerOverPanel(Vector2 gp)
    {
        var b = Instance;
        if (b == null) return false;
        if (b.shopOpen && b.shopRect.Contains(gp)) return true;
        if (b.MenuRect.Contains(gp)) return true;
        bool any = b.selectedInstance != null || b.selectedCamp != null || b.selectedCitizen != null || b.selectedEnemy != null || b.selectedGolem != null;
        if (any && b.infoPanelRect.Contains(gp)) return true;
        if (b.selectedCitizen == null && b.commandOnly != null && b.commandStripRect.Contains(gp)) return true;   // плашка свёрнутого отряда
        if (b.selectedInstance != null && (b.durabilityRect.Contains(gp) || b.actionsRect.Contains(gp))) return true;
        return false;
    }

    // Город построен (после входа в аккаунт). До этого карта пустая и не реагирует на ввод.
    private bool worldReady = false;
    private int worldSeed;
    public bool WorldReady => worldReady;
    public int WorldSeed => worldSeed;

    void Start()
    {
        Instance = this;
        cam = Camera.main;
        cam.orthographic = true;

        EnsureTypes();
        // Цены построек - из таблицы Balance (перекрывает и то, что задано в инспекторе)
        Balance.ApplyBuildings(buildingTypes);
        Balance.ApplyBuildings(new[] { townHall });
        CreateGridBackground();
        CreateGhost();

        // Карту и постройки строим, когда известен город игрока (сохранённый на сервере или новый)
        CityBoot.WhenReady(BuildWorld);
    }

    void BuildWorld(CityData saved)
    {
        CarryLedger.Clear();   // рейсы прошлого города (до перезагрузки) уже не долетят
        // Карта города строится из seed: у сохранённого города - та же самая
        worldSeed = saved != null ? saved.seed : System.Environment.TickCount & 0x7fffffff;

        // Навигация для жителей, игровые часы и зона застройки (если их нет в сцене - создаём)
        // Реки - до навигации и зон ресурсов: по ним считаются вода и плодородные берега
        Random.InitState(worldSeed);
        if (Rivers.Instance == null) Rivers.Create(mapSize);
        if (GridNavigation.Instance == null) GridNavigation.Create(mapSize);
        if (Territory.Instance == null) Territory.Create(mapSize);
        Random.InitState(worldSeed * 31 + 7);
        if (ResourceZones.Instance == null) ResourceZones.Create(mapSize);
        if (GameClock.Instance == null) new GameObject("GameClock").AddComponent<GameClock>();
        if (WarManager.Instance == null) new GameObject("WarManager").AddComponent<WarManager>();
        if (RaidManager.Instance == null) new GameObject("RaidManager").AddComponent<RaidManager>();

        if (saved != null) RestoreBuildings(saved);
        else
        {
            PlaceTownHall();
            PlaceInitialBuildingsRandomly();
        }

        // Ягодные кусты рассаживаются после стартовых зданий (в основном за пределами зелёной зоны)
        Random.InitState(worldSeed + 101);
        if (BerryField.Instance == null) BerryField.Create(berryBushes);
        Random.InitState(System.Environment.TickCount);

        // Инициализация симуляции потребления города и менеджера населения
        new GameObject("CityConsumption").AddComponent<CityConsumption>();
        new GameObject("PopulationManager").AddComponent<PopulationManager>();
        new GameObject("ManaManager").AddComponent<ManaManager>();

        // Туман войны (после Цитадели - чтобы зелёная зона уже была)
        if (FogOfWar.Home == null && !WarCityMode.Active) FogOfWar.CreateHome(mapSize);   // на войне видно по карте войны
        if (DevelopmentPath.Instance == null) new GameObject("DevelopmentPath").AddComponent<DevelopmentPath>();
        if (FieldPostManager.Instance == null) new GameObject("FieldPostManager").AddComponent<FieldPostManager>();
        if (TrapManager.Instance == null) new GameObject("TrapManager").AddComponent<TrapManager>();
        if (CrossingManager.Instance == null) new GameObject("CrossingManager").AddComponent<CrossingManager>();

        if (saved != null) CityBoot.ApplyCommon(saved);
        // Что внутри зданий (курьеры, рабочие, склады...) и запасы города - когда всё уже стоит
        if (saved != null)
        {
            JobSave.BeginLoad(AccountManager.LoggedIn && !AccountManager.IsOffline ? AwaySeconds(saved.savedUtc) : 0.0);
            PopulationManager.OfflineAway = JobSave.Away;   // жители приходят и без игрока (обновление 12)
            foreach (var r in restored) CitySave.ApplyBuilding(r.Key, r.Value);
            restored.Clear();
            CitySave.ApplyCity(saved);
            JobSave.RunAfterLoad();   // вещи, которые несли при сохранении, - на склады (обновление final-1)
        }
        if (saved != null && saved.army != null && saved.army.Count > 0) StartCoroutine(CityArmy.Restore(saved.army));
        // Пока игрока не было, город жил: доход за это время
        if (saved != null && AccountManager.LoggedIn && !AccountManager.IsOffline)
        {
            string away = CityIncome.Restore(saved, AwaySeconds(saved.savedUtc));
            if (away != null) WarManager.Notify(away, true);
        }
        worldReady = true;
    }

    // ---------- Сохранение города ----------

    /// <summary>Снимок города для сервера. null - город ещё не построен.</summary>
    // Местность города не меняется - считаем её один раз
    string terrainCode;
    int terrainSeed = -1;
    string TerrainNow()
    {
        if (terrainCode != null && terrainSeed == worldSeed) return terrainCode;
        terrainSeed = worldSeed;
        terrainCode = TerrainCode.Encode(mapSize, (x, y) =>
        {
            var cell = new Vector2Int(x, y);
            if (Rivers.IsWater(cell)) return TerrainCode.Water;
            return ResourceZones.IsForest(cell) ? TerrainCode.Forest : TerrainCode.Grass;
        });
        return terrainCode;
    }

    static double AwaySeconds(string savedUtc)
    {
        if (!System.DateTime.TryParse(savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime t)) return 0.0;
        return System.Math.Max(0.0, (WorldClock.ServerUtc - t.ToUniversalTime()).TotalSeconds);
    }

    public CityData CaptureCity()
    {
        if (!worldReady) return null;
        CityData c = new CityData { seed = worldSeed, mapSize = mapSize, terrain = TerrainNow() };
        foreach (var inst in placedInstances)
        {
            if (inst == null || inst.type == null) continue;
            SavedBuilding sb;
            c.buildings.Add(sb = new SavedBuilding
            {
                type = inst.type.name,
                x = inst.OriginCell.x,
                y = inst.OriginCell.y,
                level = inst.level,
                w = inst.type.size.x, h = inst.type.size.y,
                fortified = inst.fortified,
                starter = starterBuildings.Contains(inst.gameObject),
                gold = inst.storedGold, wood = inst.storedWood, stone = inst.storedStone, food = inst.storedFood,
                iron = inst.storedIron, oats = inst.storedOats, berries = inst.storedBerries, herbs = inst.storedHerbs,
                resin = inst.storedResin, leaves = inst.storedLeaves,
            });
            CitySave.CaptureBuilding(inst, sb);
        }
        CityBoot.CaptureCommon(c);
        CitySave.CaptureCity(c);
        CityArmy.Capture(c);
        CityIncome.Capture(c);
        return c;
    }

    /// <summary>Отпечаток построек: меняется при стройке, сносе, переносе и улучшении (тогда город сохраняется сразу).</summary>
    public int BuildingSignature()
    {
        unchecked
        {
            int h = 17;
            foreach (var inst in placedInstances)
            {
                if (inst == null || inst.type == null) continue;
                h = h * 31 + inst.type.name.GetHashCode();
                h = h * 31 + inst.OriginCell.x * 1009 + inst.OriginCell.y;
                h = h * 31 + inst.level * 2 + (inst.fortified ? 1 : 0);
                h = h * 31 + (inst.UnderConstruction ? 1 : 0) + (inst.Upgrading ? 2 : 0);   // стройка / улучшение началось или кончилось
            }
            return (h * 31 + placedInstances.Count) * 31 + CityArmy.Count();   // нанят / погиб боец - тоже сохраняем
        }
    }

    // Здания из сохранения и их записи: начинку применяем, когда город построен целиком
    readonly List<KeyValuePair<BuildingInstance, SavedBuilding>> restored = new List<KeyValuePair<BuildingInstance, SavedBuilding>>();

    // Поставить постройки сохранённого города на их места
    void RestoreBuildings(CityData d)
    {
        restored.Clear();
        bool hallPlaced = false;
        if (d.buildings != null)
            foreach (var sb in d.buildings)
            {
                if (sb == null || string.IsNullOrEmpty(sb.type)) continue;
                BuildingType t = sb.type == townHall.name ? townHall : System.Array.Find(buildingTypes, b => b != null && b.name == sb.type);
                if (t == null) { Debug.LogWarning("[Город] Неизвестная постройка в сохранении: " + sb.type); continue; }
                if (t == townHall)
                {
                    if (hallPlaced) continue;
                    hallPlaced = true;
                }

                Vector2Int origin = new Vector2Int(sb.x, sb.y);
                List<Vector2Int> footprint = GetFootprint(origin, t.size);
                bool free = true;
                foreach (var c in footprint) if (occupied.ContainsKey(c)) { free = false; break; }
                if (!free) continue;

                PlaceBuilding(t, origin, footprint, false);
                if (!occupied.TryGetValue(origin, out GameObject go) || go == null) continue;
                BuildingInstance inst = go.GetComponent<BuildingInstance>();
                if (inst == null) continue;

                inst.level = Mathf.Max(1, sb.level);
                inst.fortified = sb.fortified;
                inst.storedGold = sb.gold; inst.storedWood = sb.wood; inst.storedStone = sb.stone; inst.storedFood = sb.food;
                inst.storedIron = sb.iron; inst.storedOats = sb.oats; inst.storedBerries = sb.berries; inst.storedHerbs = sb.herbs;
                inst.storedResin = sb.resin; inst.storedLeaves = sb.leaves;
                if (sb.starter)
                {
                    starterBuildings.Add(go);
                    inst.IsStarter = true;
                }
                restored.Add(new KeyValuePair<BuildingInstance, SavedBuilding>(inst, sb));
            }
        if (!hallPlaced) PlaceTownHall();
    }

    void EnsureTypes()
    {
        // Стоимость Цитадели нужна только для улучшений (цена умножается на уровень)
        townHall = new BuildingType
        {
            name = "Ратуша",
            size = new Vector2Int(3, 3),
            costGold = 150,
            costWood = 100,
            costStone = 100,
            prodGold = 0,
            maxStorage = 100,
            color = Color.white,
            prefab = townHallCustomPrefab != null ? townHallCustomPrefab : buildingPrefab
        };

        if (buildingTypes != null && buildingTypes.Length > 0)
        {
            // Типы заданы в инспекторе - новые здания добавим сами, если их там нет
            EnsureType(BuildingInstance.RoadName, RoadType);
            EnsureType(BuildingInstance.CobbleName, CobbleType);
            EnsureType(HealerHouse.TypeName, HealerType);
            EnsureType(BuildingInstance.GateName, GateType);
            EnsureType(MagicCannonController.TypeName, CannonType);
            EnsureType(CourierHouse.TypeName, CourierType);
            EnsureType(WarehouseController.TypeName, WarehouseType);
            EnsureType(ArmoryController.TypeName, ArmoryType);
            EnsureType(StableController.TypeName, StableType);
            EnsureType(OutpostController.TypeName, OutpostType);
            EnsureType(BerryStation.TypeName, BerryStationType);
            EnsureType(KitchenController.TypeName, KitchenType);
            EnsureType(WagonYard.TypeName, YardType);
            EnsureType(PotionHouse.TypeName, PotionHouseType);
            EnsureType(WeaponForge.TypeName, ForgeType);
            EnsureType(FoundryController.TypeName, FoundryType);
            EnsureType(EngineerYard.TypeName, EngineerYardType);
            EnsureType(MarketHouse.TypeName, MarketType);
            EnsureType(BuildingInstance.BridgeName, BridgeType);
            EnsureType(BuildingInstance.GuestHouseName, GuestHouseType);
            EnsureType(GroundCannonController.TypeName, GroundCannonType);
            EnsureType(MagicArmory.TypeName, MagicArmoryType);
            EnsureType(LifeAltar.TypeName, LifeAltarType);
            EnsureType(EngineerBarracks.TypeName, EngineerBarracksType);
            EnsureType(IndustrialPlant.TypeName, IndustrialPlantType);
            EnsureType(WarBuilderYard.TypeName, WarBuilderYardType);
            
            // Если в инспекторе заданы кастомные префабы, но не привязаны через код
            foreach (var bt in buildingTypes)
            {
                if (bt != null && bt.name == "Шахта" && mineCustomPrefab != null && bt.prefab == null)
                    bt.prefab = mineCustomPrefab;
                if (bt != null && bt.name == BuildingInstance.RoadName && roadCustomPrefab != null && bt.prefab == null)
                    bt.prefab = roadCustomPrefab;
                if (bt != null && bt.name == "Ферма" && farmCustomPrefab != null && bt.prefab == null)
                    bt.prefab = farmCustomPrefab;
                if (bt != null && bt.name == "Лесопилка" && sawmillCustomPrefab != null && bt.prefab == null)
                    bt.prefab = sawmillCustomPrefab;
                if (bt != null && bt.name == "Жилой дом" && houseCustomPrefab != null && bt.prefab == null)
                    bt.prefab = houseCustomPrefab;
                if (bt != null && bt.name == "Мастерская" && workshopCustomPrefab != null && bt.prefab == null)
                    bt.prefab = workshopCustomPrefab;
                if (bt != null && bt.name == "Башня" && towerCustomPrefab != null && bt.prefab == null)
                    bt.prefab = towerCustomPrefab;
                // Мастерская и Башня - без подкраски, префаб виден в своих цветах
                if (bt != null && (bt.name == "Мастерская" || bt.name == "Башня")) bt.color = Color.white;
            }
            return;
        }

        buildingTypes = new BuildingType[]
        {
            new BuildingType { name = "Стена",      size = new Vector2Int(1, 1), costGold = 2,  costWood = 5,  costStone = 5,  color = new Color(0.6f, 0.6f, 0.65f) },
            new BuildingType { name = "Жилой дом",  size = new Vector2Int(2, 2), costGold = 40, costWood = 50, costStone = 10, maxStorage = 0, color = new Color(0.85f, 0.75f, 0.55f), prefab = houseCustomPrefab },
            new BuildingType { name = "Лесопилка",  size = new Vector2Int(2, 2), costGold = 30, costWood = 20, costStone = 10, costFood = 10, prodWood = 4, maxStorage = 60, color = Color.white, prefab = sawmillCustomPrefab },
            new BuildingType { name = "Шахта",      size = new Vector2Int(2, 2), costGold = 50, costWood = 30, costStone = 20, costFood = 15, prodGold = 1, prodStone = 3, maxStorage = 50, color = Color.white, prefab = mineCustomPrefab },
            new BuildingType { name = "Ферма",      size = new Vector2Int(3, 3), costGold = 30, costWood = 40, prodFood = 5, maxStorage = 80, color = Color.white, prefab = farmCustomPrefab },
            new BuildingType { name = "Мастерская", size = new Vector2Int(2, 2), costGold = 70, costWood = 50, costStone = 30, costFood = 10, maxStorage = 0, color = Color.white, prefab = workshopCustomPrefab },
            new BuildingType { name = BarracksController.TypeName, size = new Vector2Int(2, 2), costGold = 100,costWood = 80, costStone = 40, costFood = 30, maxStorage = 0, color = new Color(0.7f, 0.2f, 0.2f) },
            new BuildingType { name = "Башня",      size = new Vector2Int(2, 2), costGold = 80, costWood = 30, costStone = 60, costFood = 20, maxStorage = 0, color = Color.white, prefab = towerCustomPrefab },
            RoadType(),
            HealerType(),
            GateType(),
            CannonType(),
            CourierType(),
            WarehouseType(),
            ArmoryType(),
            StableType(),
            OutpostType(),
            CobbleType(),
            BerryStationType(),
            KitchenType(),
            YardType(),
            PotionHouseType(),
            ForgeType(),
            FoundryType(),
            EngineerYardType(),
            MarketType(),
            BridgeType(),
            GuestHouseType(),
            GroundCannonType(),
            MagicArmoryType(),
            LifeAltarType(),
            EngineerBarracksType(),
            IndustrialPlantType(),
            WarBuilderYardType(),
        };
    }

    // Пушка: ядра прямой наводкой (Цитадель Ур.3, любой путь). Первая - уже со сталью (UpgradeRules)
    static BuildingType GroundCannonType()
    {
        return new BuildingType { name = GroundCannonController.TypeName, size = new Vector2Int(2, 2), costGold = 180, costWood = 60, costStone = 120, costFood = 20, maxStorage = 0, color = new Color(0.42f, 0.4f, 0.36f) };
    }

    // Магическая оружейная: снаряды для пушек (путь Магии, Цитадель Ур.3)
    static BuildingType MagicArmoryType()
    {
        return new BuildingType { name = MagicArmory.TypeName, size = new Vector2Int(2, 2), costGold = 160, costWood = 60, costStone = 80, costFood = 20, maxStorage = 0, color = new Color(0.55f, 0.4f, 0.85f) };
    }

    // Алтарь жизни: растит голема из подношений (школа Магии жизни, Цитадель Ур.3, один на город)
    static BuildingType LifeAltarType()
    {
        return new BuildingType { name = LifeAltar.TypeName, size = new Vector2Int(2, 2), costGold = 200, costWood = 40, costStone = 120, costFood = 30, maxStorage = 0, color = new Color(0.4f, 0.75f, 0.45f) };
    }

    // Инженерная казарма: катапульты (Инженерия, направление «Военный инженер», Цитадель Ур.3, одна на город)
    static BuildingType EngineerBarracksType()
    {
        return new BuildingType { name = EngineerBarracks.TypeName, size = new Vector2Int(3, 3), costGold = 260, costWood = 120, costStone = 80, costFood = 20, maxStorage = 0, color = new Color(0.58f, 0.4f, 0.24f) };
    }

    // Завод: площадка, производства рядом работают быстрее на топливе (Инженерия, «Индустриализатор», Цитадель Ур.3, один)
    static BuildingType IndustrialPlantType()
    {
        return new BuildingType { name = IndustrialPlant.TypeName, size = new Vector2Int(3, 3), costGold = 300, costWood = 100, costStone = 160, costFood = 0, maxStorage = 0, color = new Color(0.45f, 0.52f, 0.6f) };
    }

    // Двор военного строителя: строители чинят здания в бою (Военное дело, «Военный строитель», Цитадель Ур.3, один)
    static BuildingType WarBuilderYardType()
    {
        return new BuildingType { name = WarBuilderYard.TypeName, size = new Vector2Int(3, 2), costGold = 220, costWood = 160, costStone = 80, costFood = 20, maxStorage = 0, color = new Color(0.78f, 0.52f, 0.26f) };
    }

    // Мост: клетка моста через реку (тянется, как дорожка). Цитадель Ур.2, на клетку ещё 1 доска (UpgradeRules)
    BuildingType BridgeType()
    {
        return new BuildingType { name = BuildingInstance.BridgeName, size = new Vector2Int(1, 1), costWood = 8, costStone = 4, maxStorage = 0, color = new Color(0.55f, 0.38f, 0.2f) };
    }

    // Гостевой дом: живут только наёмники (6 / 12 / 18 мест)
    BuildingType GuestHouseType()
    {
        return new BuildingType { name = BuildingInstance.GuestHouseName, size = new Vector2Int(2, 2), costGold = 60, costWood = 60, costStone = 20, maxStorage = 0, color = new Color(0.72f, 0.55f, 0.85f) };
    }

    // Добавить тип в конец списка, если здания с таким именем ещё нет (типы из инспектора не трогаем)
    void EnsureType(string typeName, System.Func<BuildingType> make)
    {
        foreach (var bt in buildingTypes)
            if (bt != null && bt.name == typeName) return;

        var list = new List<BuildingType>(buildingTypes);
        list.Add(make());
        buildingTypes = list.ToArray();
    }

    BuildingType RoadType()
    {
        return new BuildingType { 
            name = BuildingInstance.RoadName, 
            size = new Vector2Int(1, 1), 
            costWood = 2, 
            maxStorage = 0, 
            color = Color.white, 
            prefab = roadCustomPrefab != null ? roadCustomPrefab : buildingPrefab 
        };
    }

    static BuildingType BerryStationType()
    {
        return new BuildingType { name = BerryStation.TypeName, size = new Vector2Int(2, 2), costGold = 40, costWood = 55, costStone = 10, costFood = 15, maxStorage = 40, color = new Color(0.62f, 0.25f, 0.5f) };
    }

    static BuildingType KitchenType()
    {
        return new BuildingType { name = KitchenController.TypeName, size = new Vector2Int(2, 2), costGold = 110, costWood = 80, costStone = 65, costFood = 30, maxStorage = 0, color = new Color(0.88f, 0.48f, 0.32f) };
    }

    static BuildingType PotionHouseType()
    {
        return new BuildingType { name = PotionHouse.TypeName, size = new Vector2Int(2, 2), costGold = 120, costWood = 60, costStone = 60, maxStorage = 0, color = new Color(0.5f, 0.8f, 0.55f) };
    }

    static BuildingType ForgeType()
    {
        return new BuildingType { name = WeaponForge.TypeName, size = new Vector2Int(2, 2), costGold = 150, costWood = 60, costStone = 100, maxStorage = 0, color = new Color(0.55f, 0.35f, 0.3f) };
    }

    static BuildingType FoundryType()
    {
        return new BuildingType { name = FoundryController.TypeName, size = new Vector2Int(3, 2), costGold = 140, costWood = 70, costStone = 120, costFood = 20, maxStorage = 0, color = new Color(0.78f, 0.45f, 0.22f) };
    }

    static BuildingType EngineerYardType()
    {
        return new BuildingType { name = EngineerYard.TypeName, size = new Vector2Int(4, 4), costGold = 220, costWood = 150, costStone = 150, costFood = 30, maxStorage = 0, color = new Color(0.85f, 0.72f, 0.38f) };
    }

    static BuildingType MarketType()
    {
        return new BuildingType { name = MarketHouse.TypeName, size = new Vector2Int(3, 3), costGold = 160, costWood = 120, costStone = 90, costFood = 20, maxStorage = 0, color = new Color(0.95f, 0.78f, 0.3f) };
    }

    static BuildingType YardType()
    {
        return new BuildingType { name = WagonYard.TypeName, size = new Vector2Int(3, 2), costGold = 300, costWood = 200, costStone = 150, costFood = 0, maxStorage = 0, color = new Color(0.25f, 0.5f, 0.72f) };
    }

    static BuildingType OutpostType()
    {
        return new BuildingType { name = OutpostController.TypeName, size = new Vector2Int(2, 2), costGold = 250, costWood = 150, costStone = 150, costFood = 40, maxStorage = 0, color = new Color(0.95f, 0.65f, 0.2f) };
    }

    static BuildingType CourierType()
    {
        return new BuildingType { name = CourierHouse.TypeName, size = new Vector2Int(2, 2), costGold = 90, costWood = 60, costStone = 40, costFood = 20, maxStorage = 0, color = new Color(0.9f, 0.5f, 0.65f) };
    }

    static BuildingType WarehouseType()
    {
        return new BuildingType { name = WarehouseController.TypeName, size = new Vector2Int(3, 2), costGold = 60, costWood = 90, costStone = 50, maxStorage = 0, color = new Color(0.62f, 0.5f, 0.38f) };
    }

    static BuildingType ArmoryType()
    {
        return new BuildingType { name = ArmoryController.TypeName, size = new Vector2Int(3, 2), costGold = 100, costWood = 60, costStone = 60, maxStorage = 0, color = new Color(0.45f, 0.5f, 0.62f) };
    }

    static BuildingType StableType()
    {
        return new BuildingType { name = StableController.TypeName, size = new Vector2Int(3, 2), costGold = 90, costWood = 115, costStone = 40, costFood = 25, maxStorage = 0, color = new Color(0.55f, 0.36f, 0.2f) };
    }

    static BuildingType GateType()
    {
        return new BuildingType { name = BuildingInstance.GateName, size = new Vector2Int(1, 1), costGold = 10, costWood = 20, costStone = 15, maxStorage = 0, color = new Color(0.5f, 0.5f, 0.56f) };
    }

    static BuildingType CannonType()
    {
        return new BuildingType { name = MagicCannonController.TypeName, size = new Vector2Int(2, 2), costGold = 120, costWood = 40, costStone = 80, costFood = 20, maxStorage = 0, color = new Color(0.6f, 0.35f, 0.9f) };
    }

    static BuildingType HealerType()
    {
        return new BuildingType { name = HealerHouse.TypeName, size = new Vector2Int(2, 2), costGold = 60, costWood = 40, costStone = 20, costFood = 20, maxStorage = 0, color = new Color(0.45f, 0.85f, 0.75f) };
    }

    static BuildingType CobbleType()
    {
        return new BuildingType { name = BuildingInstance.CobbleName, size = new Vector2Int(1, 1), maxStorage = 0, color = new Color(0.72f, 0.7f, 0.66f) };
    }

    void CreateGridBackground()
    {
        int cellPx = 16;
        int res = mapSize * cellPx;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        var rng = new System.Random(12345);
        float offX = rng.Next(0, 1000), offY = rng.Next(0, 1000);

        Color[] pixels = new Color[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float big = Mathf.PerlinNoise(offX + x * 0.02f, offY + y * 0.02f);
                float mid = Mathf.PerlinNoise(offX + x * 0.12f, offY + y * 0.12f);
                float t = Mathf.Clamp01(big * 0.65f + mid * 0.35f);
                Color c = Color.Lerp(grassDark, grassLight, t);

                double r = rng.NextDouble();
                if (r < 0.06) c = Color.Lerp(c, grassLight * 1.25f, 0.6f);
                else if (r < 0.12) c = Color.Lerp(c, grassDark * 0.7f, 0.6f);

                if (r > 0.9985) c = new Color(0.95f, 0.9f, 0.4f);

                if (showGridLines && (x % cellPx == 0 || y % cellPx == 0))
                    c = Color.Lerp(c, Color.black, gridLineStrength);

                c.a = 1f;
                pixels[y * res + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        GameObject bg = PlainQuad.Create();
        bg.name = "GameMap_Background";
        bg.transform.position = new Vector3(0f, 0f, 1f);
        bg.transform.localScale = new Vector3(mapSize, mapSize, 1f);

        Material mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        bg.GetComponent<Renderer>().material = mat;
        bg.GetComponent<Renderer>().sortingOrder = -100;
        // На войне землю под городом рисует карта войны (та же трава, реки и леса текут дальше) - иначе виден квадрат
        if (WarCityMode.Active) bg.GetComponent<Renderer>().enabled = false;
    }

    void CreateGhost()
    {
        if (buildingPrefab == null) return;
        ghost = Instantiate(buildingPrefab);
        ghost.name = "BuildingGhost";
        foreach (var col in ghost.GetComponentsInChildren<Collider2D>()) Destroy(col);
        foreach (var col in ghost.GetComponentsInChildren<Collider>()) Destroy(col);
        ghost.SetActive(false);
    }

    void PlaceTownHall()
    {
        Vector2Int origin = new Vector2Int(-1, -1);
        PlaceBuilding(townHall, origin, GetFootprint(origin, townHall.size), false);
    }

    void PlaceInitialBuildingsRandomly()
    {
        BuildingType sawmillType = System.Array.Find(buildingTypes, b => b.name == "Лесопилка");
        BuildingType mineType = System.Array.Find(buildingTypes, b => b.name == "Шахта");
        BuildingType farmType = System.Array.Find(buildingTypes, b => b.name == "Ферма");
        BuildingType houseType = System.Array.Find(buildingTypes, b => b.name == "Жилой дом");

        BuildingType[] initialToSpawn = new BuildingType[] { sawmillType, mineType, farmType, houseType };

        foreach (var bType in initialToSpawn)
        {
            if (bType == null) continue;

            bool placed = false;
            int attempts = 0;

            while (!placed && attempts < 200)
            {
                attempts++;
                int rx = Random.Range(-10, 10 - bType.size.x + 1);
                int ry = Random.Range(-10, 10 - bType.size.y + 1);
                Vector2Int origin = new Vector2Int(rx, ry);
                List<Vector2Int> footprint = GetFootprint(origin, bType.size);

                if (CellsFree(footprint) && ZoneOk(bType, footprint) && WaterProblem(bType, footprint) == "" && SameBankAsHall(footprint)
                    && DoorProblem(bType, origin, footprint, null) == 0)
                {
                    PlaceBuilding(bType, origin, footprint, false);
                    if (occupied.TryGetValue(origin, out GameObject starter) && starter != null)
                    {
                        starterBuildings.Add(starter);
                        BuildingInstance si = starter.GetComponent<BuildingInstance>();
                        if (si != null) si.IsStarter = true;   // стартовые лесопилка, шахта и ферма восстанавливаются даром
                    }
                    placed = true;
                }
            }
        }
    }

    // Стартовое здание - на том же берегу, что и Цитадель (иначе без моста до него не дойти)
    static bool SameBankAsHall(List<Vector2Int> footprint)
    {
        if (!Rivers.Any) return true;
        int hallBank = Rivers.LandRegion(new Vector2Int(0, -3));
        if (hallBank == 0) hallBank = Rivers.LandRegion(new Vector2Int(-2, 0));
        foreach (var c in footprint)
        {
            if (Rivers.LandRegion(c) != hallBank) return false;
            // и дверь (клетки вокруг) тоже не за рекой
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int r = Rivers.LandRegion(new Vector2Int(c.x + dx, c.y + dy));
                    if (r != 0 && r != hallBank) return false;
                }
        }
        return true;
    }

    bool BuildBlocked => WarManager.IsWar || WarCityMode.Active;   // военный город: строить, сносить и переносить нельзя

    void UpdateTerritoryOverlay()
    {
        string hintType = isMoving ? (movingBuildingType != null ? movingBuildingType.name : null)
                        : (isBuildingMode && Current != null ? Current.name : null);
        ResourceZones.HintShow = hintType != null && ResourceZones.IsZoneBuilding(hintType);

        if (Territory.Instance == null) return;

        bool show = shopOpen || isBuildingMode || isMoving;
        bool frontier = false;

        if (isBuildingMode && !isMoving && Current != null) frontier = IsFrontierType(Current.name);
        if (isMoving && movingBuildingType != null) frontier = IsFrontierType(movingBuildingType.name);

        if (!show && selectedInstance != null && selectedInstance.type != null &&
            (selectedInstance.IsTownHall || selectedInstance.IsOutpost)) show = true;

        Territory.Instance.SetOverlay(show, frontier);
    }

    static bool IsFrontierType(string typeName)
    {
        return BuildingInstance.IsRoadName(typeName) || typeName == OutpostController.TypeName;
    }

    // Тревога (WarManager.RaiseAlarm): свернуть окно, чтобы было видно бой
    void OnEnable() { WarManager.OnAlarm += CollapseForAlarm; }
    void OnDisable() { WarManager.OnAlarm -= CollapseForAlarm; }
    bool CollapseForAlarm()   // только список построек; постройка «в руке» остаётся (при войне её снимет Update)
    {
        if (!shopOpen) return false;
        SetShopOpen(false);
        return true;
    }

    /// <summary>Здание «в руках»: ставят новое или переносят (обновляется каждый кадр).</summary>
    public static bool PlacingNow { get; private set; }
    /// <summary>В руках переносимое здание (не новое).</summary>
    public static bool MovingNow { get; private set; }
    /// <summary>Выбранное сейчас здание (или null). Обновляется каждый кадр.</summary>
    public static BuildingInstance Selected { get; private set; }

    void OnDestroy() { PlacingNow = MovingNow = false; Selected = null; if (Instance == this) Instance = null; }

    void LateUpdate()
    {
        PlacingNow = isBuildingMode || isMoving;
        MovingNow = isMoving;
        Selected = selectedInstance != null && selectedInstance.gameObject != null ? selectedInstance : null;
    }

    /// <summary>Мышь над окнами города (карта войны вокруг города тогда не берёт щелчок).</summary>
    public static bool PointerOverUI { get; private set; }

    void Update()
    {
        PointerOverUI = shopOpen || DemolishDialog.IsOpen || ConfirmDialog.IsOpen || AccountManager.Blocking;
        PlacingNow = isBuildingMode || isMoving;
        MovingNow = isMoving;
        Selected = selectedInstance != null && selectedInstance.gameObject != null ? selectedInstance : null;
        // F7 (админ, для тестов): закончить все стройки и улучшения сразу
        if (GameAdmin.On && worldReady && Keyboard.current != null && !UiKit.Typing && Keyboard.current.f7Key.wasPressedThisFrame) BuildingInstance.AdminSkipAll();
        if (Mouse.current == null || cam == null || !worldReady) return;

        if (RaidManager.ViewingBattle || WarView.IsOpen)
        {
            if (ghost != null) ghost.SetActive(false);
            if (shopOpen) SetShopOpen(false);
            if (isBuildingMode) StopBuilding();
            if (isMoving) CancelMove();
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        float guiY = UiKit.ToGui(mousePos).y;
        mousePos.x = UiKit.ToGui(mousePos).x;   // дальше mousePos.x - в точках окон (как guiY)

        bool war = BuildBlocked;
        if (war && !wasWar && (isBuildingMode || isMoving))   // открытый список закрывает тревога со своим сообщением
            WarManager.Notify("Режим войны — строительство остановлено", false);
        wasWar = war;
        if (war)
        {
            if (shopOpen) SetShopOpen(false);
            if (isBuildingMode) StopBuilding();
            if (isMoving) CancelMove();
            if (DemolishDialog.IsOpen)
            {
                DemolishDialog.Close();
                WarManager.Notify("Идёт война — снос отменён", false);
            }
        }

        // Пока открыт вопрос «Снести?» / «Точно?» или окно входа - карта не реагирует на мышь и клавиши
        if (DemolishDialog.IsOpen || ConfirmDialog.IsOpen || AccountManager.Blocking)
        {
            if (ghost != null) ghost.SetActive(false);
            return;
        }

        UpdateTerritoryOverlay();

        if (shopOpen)
        {
            if (ghost != null) ghost.SetActive(false);
            CameraController.BlockZoom();
            float wheel = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f) shopScroll = Mathf.Clamp(shopScroll - Mathf.Sign(wheel) * 70f, 0f, shopMaxScroll);
            float drag = TouchInput.ScrollDragGui;   // телефон: список едет ровно за пальцем
            if (drag != 0f) shopScroll = Mathf.Clamp(shopScroll - drag, 0f, shopMaxScroll);
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SetShopOpen(false);

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 gp = new Vector2(mousePos.x, guiY);
                bool onWindow = shopRect.Contains(gp);
                bool onButtons = MenuRect.Contains(gp) || RaidManager.IsPointerOverUI(gp) || DevelopmentPath.IsPointerOver(gp) || WeaponForge.IsPointerOver(gp) || MarketWindow.IsPointerOver(gp) || CamoScouts.Blocks(gp)
                    || (GameMenu.Instance != null && GameMenu.Instance.IsPointerOver(gp))
                    || (GameClock.Instance != null && GameClock.Instance.IsPointerOver(gp));
                if (!onWindow && !onButtons) SetShopOpen(false);
            }
            return;
        }

        bool mouseOverMenu = MenuRect.Contains(new Vector2(mousePos.x, guiY));
        bool mouseOverInfo = ((selectedInstance != null || selectedCamp != null || selectedCitizen != null || selectedEnemy != null || selectedGolem != null) && infoPanelRect.Contains(new Vector2(mousePos.x, guiY)))
            || (selectedCitizen == null && commandOnly != null && commandStripRect.Contains(new Vector2(mousePos.x, guiY)))   // плашка свёрнутого отряда (final-9)
            || (selectedInstance != null && durabilityRect.Contains(new Vector2(mousePos.x, guiY)))    // плашка прочности под панелью
            || (selectedInstance != null && actionsRect.Contains(new Vector2(mousePos.x, guiY)));      // кнопки «Перенести» / «Снести» под ней

        if (selectedCitizen != null && selectedCitizen.IsDead) selectedCitizen = null;
        if (selectedGolem != null && (!selectedGolem.isActiveAndEnabled || selectedGolem.Altar == null || !selectedGolem.Altar.GolemAlive)) selectedGolem = null;
        if (selectedGolem != null && GolemInfoPanel.Follow && CameraController.Instance != null)
            CameraController.Instance.FocusOn(selectedGolem.Position);
        if (selectedEnemy != null && (!EnemyAgent.Alive(selectedEnemy) || !FogOfWar.IsVisible(selectedEnemy.Position))) selectedEnemy = null;
        if (selectedEnemy != null && EnemyInfoPanel.Follow && CameraController.Instance != null)
            CameraController.Instance.FocusOn(selectedEnemy.Position);
        if (selectedCitizen != null && CitizenInfoPanel.Follow && CameraController.Instance != null)
        {
            // final-13-fix-3: в здании - камера держит само здание (над ним звёздочка избранного)
            BuildingInstance inside = selectedCitizen.InsideBuilding;
            CameraController.Instance.FocusOn(inside != null ? inside.Center : (Vector2)selectedCitizen.transform.position);
        }
        bool mouseOverCity = GameMenu.Instance != null && GameMenu.Instance.IsPointerOver(new Vector2(mousePos.x, guiY));
        bool mouseOverClock = GameClock.Instance != null && GameClock.Instance.IsPointerOver(new Vector2(mousePos.x, guiY));
        Vector2 guiMouse = new Vector2(mousePos.x, guiY);
        bool mouseOverRaid = RaidManager.IsPointerOverUI(guiMouse) || DevelopmentPath.IsPointerOver(guiMouse) || WeaponForge.IsPointerOver(guiMouse) || MarketWindow.IsPointerOver(guiMouse) || FieldPostManager.Blocks(guiMouse) || TrapManager.Blocks(guiMouse) || CrossingManager.Blocks(guiMouse) || CamoScouts.Blocks(guiMouse)
            || AccountManager.IsPointerOver(guiMouse) || MobileInput.Busy;
        bool mouseOverUI = mouseOverMenu || mouseOverInfo || mouseOverCity || mouseOverClock || mouseOverRaid;
        PointerOverUI = mouseOverMenu || mouseOverInfo || mouseOverCity || mouseOverClock || (RaidManager.IsPointerOverUI(guiMouse) || DevelopmentPath.IsPointerOver(guiMouse) || WeaponForge.IsPointerOver(guiMouse) || MarketWindow.IsPointerOver(guiMouse) || ClanWindow.IsPointerOver(guiMouse) || DefenseWindow.IsPointerOver(guiMouse) || StampShop.IsPointerOver(guiMouse) || CityShield.IsPointerOver(guiMouse) || ReplayViewer.ChipOver(guiMouse)) || MobileInput.Busy;
        if (mouseOverInfo || mouseOverCity) CameraController.BlockZoom();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (isMoving) { CancelMove(); return; }
            if (isBuildingMode) { StopBuilding(); return; }
        }

        if (Mouse.current.rightButton.wasPressedThisFrame && isBuildingMode)
        {
            StopBuilding();
            return;
        }

        BarracksController pickBarracks = (selectedInstance != null && !isMoving && !isBuildingMode)
            ? selectedInstance.GetComponent<BarracksController>() : null;
        if (pickBarracks != null && pickBarracks.PickingTarget)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                pickBarracks.PickingTarget = false;
            }
            else if (Mouse.current.leftButton.wasPressedThisFrame && !mouseOverUI)
            {
                Vector3 pw = MouseWorld();
                pickBarracks.SendSelectedTo(new Vector2(pw.x, pw.y));
            }
            return;
        }

        if (!isMoving && !isBuildingMode) HandleHotkeys();

        if (isMoving)
        {
            if (ghost != null) ghost.SetActive(true);
            Vector2Int origin = GetOriginUnderMouse(movingBuildingType.size);
            List<Vector2Int> footprint = GetFootprint(origin, movingBuildingType.size);

            placeWaterProblem = EdgeProblem(footprint);
            if (placeWaterProblem == "") placeWaterProblem = WaterProblem(movingBuildingType, footprint);
            bool spaceAvailable = CanBuildWhileMoving(footprint, movingBuilding) && ZoneOk(movingBuildingType, footprint) && placeWaterProblem == "";
            placeDoorProblem = spaceAvailable ? DoorProblem(movingBuildingType, origin, footprint, movingBuilding) : 0;
            spaceAvailable = spaceAvailable && placeDoorProblem == 0;
            UpdateGhost(movingBuildingType, origin, spaceAvailable);

            if (Mouse.current.leftButton.wasPressedThisFrame && spaceAvailable && !mouseOverUI)
            {
                ConfirmMove(origin, footprint);
            }
            return;
        }

        if (isBuildingMode)
        {
            BuildingType activeType = Current;
            Vector2Int origin = GetOriginUnderMouse(activeType.size);
            List<Vector2Int> footprint = GetFootprint(origin, activeType.size);

            // Ворота можно ставить прямо на дорогу (final-6): клетка дороги сама уступает место воротам
            bool gateOnRoad = activeType.name == BuildingInstance.GateName && AllRoad(footprint);
            bool freeCells = gateOnRoad || CellsFree(footprint);
            placeWaterProblem = EdgeProblem(footprint);
            if (placeWaterProblem == "") placeWaterProblem = WaterProblem(activeType, footprint);
            bool zoneOk = placeWaterProblem == "" && (gateOnRoad || ZoneOk(activeType, footprint));
            placeDoorProblem = (freeCells && zoneOk) ? DoorProblem(activeType, origin, footprint, null) : 0;
            bool affordable = CanAfford(activeType);
            bool canBuild = freeCells && zoneOk && placeDoorProblem == 0 && affordable && !mouseOverUI && !BuildBlocked;
            placeSpaceOk = freeCells;
            placeZoneOk = zoneOk;

            UpdateGhost(activeType, origin, canBuild);

            bool paint = IsPaintType(activeType);

            if (placeGuard && !Mouse.current.leftButton.isPressed) placeGuard = false;
            bool placeClick = !placeGuard && (paint ? Mouse.current.leftButton.isPressed : Mouse.current.leftButton.wasPressedThisFrame);

            if (placeClick && canBuild)
            {
                if (gateOnRoad) RemoveRoadsQuiet(footprint);
                PlaceBuilding(activeType, origin, footprint, true);

                bool keepInHand = paint || (Keyboard.current != null && Keyboard.current.shiftKey.isPressed);
                if (!keepInHand) StopBuilding();
            }
        }
        else
        {
            if (ghost != null) ghost.SetActive(false);

            if (Mouse.current.leftButton.wasPressedThisFrame && !mouseOverUI)
            {
                TrySelectBuildingUnderMouse();
            }

            // Esc (на телефоне - «Назад») закрывает окно выбранного (final-6), если поверх нет другого окна
            if (!UiKit.Typing && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame
                && (selectedInstance != null || selectedCamp != null || selectedCitizen != null || selectedEnemy != null || selectedGolem != null || commandOnly != null)
                && !MarketWindow.IsOpen && !StampShop.IsOpen && !ReportDialog.IsOpen && !ClanWindow.EatsEscape)
            {
                if (selectedCitizen == null && commandOnly != null) ReleaseCommand();   // второй Esc - отпустить отряд
                else CloseSelection(true);
            }

            if (!mouseOverUI && !UiKit.Typing && Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                StartMoveBuildingUnderMouse();
            }

            if (!mouseOverUI && !UiKit.Typing && Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
            {
                AskDemolishUnderMouse();
            }
        }
    }

    // Окно бойца закрыли, а приказ «Послать отряд» был включён (final-9): отряд остаётся под рукой.
    // Нажатие по карте - приказ, как при открытом окне; сверху - узкая плашка с «✕», чтобы отпустить отряд.
    CitizenAgent commandOnly;

    /// <summary>Сейчас нажатие по карте - приказ отряду выбранного бойца (окно открыто или свёрнуто в плашку).</summary>
    public static bool CommandingSquad
    {
        get
        {
            var b = Instance;
            if (b == null || !CitizenInfoPanel.OrderArmed) return false;
            CitizenAgent a = b.selectedCitizen != null ? b.selectedCitizen : b.commandOnly;
            return CitizenInfoPanel.Orderable(a);
        }
    }

    void TrySelectBuildingUnderMouse()
    {
        Vector3 w = MouseWorld();

        // Прямой приказ (final-8): выбран боец и включено «Послать отряд» - нажатие по траве или по врагу = приказ
        CitizenAgent cmdAgent = selectedCitizen != null ? selectedCitizen : commandOnly;
        if (commandOnly != null && !CitizenInfoPanel.Orderable(commandOnly)) commandOnly = null;   // погиб, ушёл в поход
        if (cmdAgent != null && CitizenInfoPanel.OrderArmed && CitizenInfoPanel.Orderable(cmdAgent))
        {
            Vector2 p2 = new Vector2(w.x, w.y);
            CitizenAgent hitC = CitizenAgent.PickAt(p2);
            bool hitB = occupied.TryGetValue(new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.y)), out GameObject hb) && hb != null
                        && !(hb.GetComponent<BuildingInstance>() is BuildingInstance hbi && (hbi.IsRoad || hbi.IsRealRuin));
            // Свой боец того же отряда под пальцем - тоже приказ (иначе нажатие рядом с отрядом его «перевыбирало»)
            if (hitC != null && cmdAgent.Squad != null && hitC.Squad == cmdAgent.Squad) hitC = null;
            EnemyAgent foe0 = EnemyAgent.PickAt(p2);
            if (foe0 != null && !FogOfWar.IsVisible(foe0.Position)) foe0 = null;
            if (foe0 != null) { hitC = null; hitB = false; }   // нажали по врагу - это приказ напасть, даже если он у здания
            if (hitC == null && !hitB)
            {
                Vector2 to = foe0 != null ? (Vector2)foe0.Position : p2;
                if (cmdAgent.ExpBarracks.SendSquad(cmdAgent.Squad, to, foe0))
                {
                    Sfx.Ui("ui_click");
                    CombatFx.Spark(to, new Color(0.4f, 1f, 1f), 0.25f, 0.4f);
                }
                return;   // боец остаётся выбранным: можно сразу дать новый приказ
            }
        }
        commandOnly = null;   // нажали по зданию или жителю - обычный выбор, свёрнутый отряд отпускаем

        EnemyAgent foe = EnemyAgent.PickAt(new Vector2(w.x, w.y));
        if (foe != null && !FogOfWar.IsVisible(foe.Position)) foe = null;
        selectedEnemy = foe;
        selectedGolem = null;
        if (foe != null)
        {
            selectedCitizen = null;
            selectedInstance = null;
            selectedCamp = null;
            EnemyInfoPanel.Follow = false;
            return;
        }

        // Голем Алтаря жизни: выбрать, как бойца (приказов он не слушает - только карточка)
        LifeGolem golem = LifeGolem.PickAt(new Vector2(w.x, w.y));
        if (golem != null)
        {
            selectedGolem = golem;
            selectedCitizen = null;
            selectedInstance = null;
            selectedCamp = null;
            GolemInfoPanel.Follow = false;
            Sfx.Ui("ui_click");
            return;
        }

        CitizenAgent who = CitizenAgent.PickAt(new Vector2(w.x, w.y));
        if (who != selectedCitizen) CitizenInfoPanel.OrderArmed = who != null && WarManager.IsWar && CitizenInfoPanel.Orderable(who);   // на войне - приказ сразу
        selectedCitizen = who;
        if (who != null)
        {
            selectedInstance = null;
            selectedCamp = null;
            CitizenInfoPanel.Follow = false;
            return;
        }

        selectedCamp = null;
        foreach (var camp in CampMarker.All)
        {
            if (camp != null && camp.Hit(new Vector2(w.x, w.y))) { selectedCamp = camp; break; }
        }
        if (selectedCamp != null)
        {
            selectedInstance = null;
            return;
        }
        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.y));

        if (occupied.TryGetValue(cell, out GameObject clickedObj))
        {
            BuildingInstance inst = clickedObj.GetComponent<BuildingInstance>();
            if (inst != null && inst.type != null && inst.IsRoad)
            {
                selectedInstance = null;
                return;
            }
            if (inst != null && inst != selectedInstance) Sfx.Ui("ui_click");   // щелчок слышно и при переходе с одного здания на другое
            selectedInstance = inst;
            if (inst != null && !inst.UnderConstruction && inst.GetComponent<MarketHouse>() != null) MarketWindow.Open();   // стройка - ещё не рынок
        }
        else
        {
            selectedInstance = null;
        }
    }

    public int CountOfType(string typeName)
    {
        int n = 0;
        foreach (var inst in placedInstances)
            if (inst != null && inst.type != null && inst.type.name == typeName) n++;
        return n;
    }

    void CollectFrom(BuildingInstance inst)
    {
        if (inst == null) return;
        var pm = PopulationManager.Instance;
        if (pm == null || pm.Hall == null) inst.CollectToTreasury();
        else pm.DispatchCourier(inst);
    }

    void CollectAllOfType(BuildingType targetType)
    {
        foreach (var inst in placedInstances)
        {
            if (inst == null) continue;
            if (inst.type == targetType || (inst.type != null && targetType != null && inst.type.name == targetType.name))
            {
                CollectFrom(inst);
            }
        }
    }

    void HandleHotkeys()
    {
        if (Keyboard.current == null || UiKit.Typing) return;   // печатают в поле (чат клана и т.п.) - клавиши не трогаем
        int count = Mathf.Min(9, buildingTypes.Length);
        for (int i = 0; i < count; i++)
        {
            if (Keyboard.current[Key.Digit1 + i].wasPressedThisFrame) TakeInHand(i);
        }
    }

    void SetShopOpen(bool open)
    {
        if (open && BuildBlocked)
        {
            WarManager.Notify("Идёт война — строить нельзя", false);
            return;
        }

        shopOpen = open;
        if (!open) return;
        shopScroll = 0f;
        UiKit.Front("shop");   // меню «Город», путь и т.п. закроются - не лягут на окно построек

        StopBuilding();
        selectedInstance = null;
        selectedCamp = null;
        selectedCitizen = null;
        selectedEnemy = null;
        selectedGolem = null;
    }

    void TakeInHand(int index)
    {
        if (index < 0 || index >= buildingTypes.Length) return;
        if (BuildBlocked)
        {
            WarManager.Notify("Идёт война — строить нельзя", false);
            return;
        }

        selected = index;
        isBuildingMode = true;
        shopOpen = false;
        placeGuard = true;
        selectedInstance = null;
        selectedCamp = null;
        selectedCitizen = null;
        selectedEnemy = null;
        selectedGolem = null;
    }

    void StopBuilding()
    {
        isBuildingMode = false;
        if (ghost != null) ghost.SetActive(false);
    }

    /// <summary>В руках дорога, стена или мост - их удобно «тянуть» пальцем линией.</summary>
    public static bool PlacingPaintType => Instance != null && Instance.isBuildingMode && !Instance.isMoving && IsPaintType(Instance.Current);

    static bool IsPaintType(BuildingType t)
    {
        return t != null && (BuildingInstance.IsRoadName(t.name) || t.name == BuildingInstance.WallName || t.name == BuildingInstance.BridgeName);
    }

    bool HasResources(BuildingType t)
    {
        var rm = ResourceManager.Instance;
        Balance.NextBuildCost(t, out int g, out int w, out int s, out int f);   // 2-е, 3-е здание дороже
        return rm != null && rm.HasEnough(g, w, s, f);
    }

    bool CanAfford(BuildingType t)
    {
        return HasResources(t) && UpgradeRules.CanPayBuild(t.name, CountOfType(t.name)) && !AtLimit(t) && HallLevelOk(t) && NeedProblem(t) == "" && PathAllows(t.name);
    }

    static string CostLine(BuildingType t)
    {
        var rm = ResourceManager.Instance;
        Balance.NextBuildCost(t, out int g, out int w, out int s, out int f);
        List<string> parts = new List<string>();
        AddCost(parts, g, rm != null ? rm.gold : 0, "золота");
        AddCost(parts, w, rm != null ? rm.wood : 0, "дерева");
        AddCost(parts, s, rm != null ? rm.stone : 0, "камня");
        AddCost(parts, f, rm != null ? rm.food : 0, "еды");
        float sec = Balance.NextBuildSeconds(t);
        if (sec > 0f) parts.Add(UiKit.Col(UiKit.MutedHex, "◷ " + Balance.Dur(sec)));
        return parts.Count > 0 ? string.Join(UiKit.Col(UiKit.MutedHex, " · "), parts) : UiKit.Col(UiKit.MutedHex, t.name == BuildingInstance.CobbleName ? "только плитка" : "бесплатно");
    }

    static void AddCost(List<string> parts, int need, int have, string what)
    {
        if (need <= 0) return;
        string hex = what == "золота" ? UiKit.GoldHex : (what == "дерева" ? UiKit.WoodHex : (what == "камня" ? UiKit.StoneHex : UiKit.FoodHex));
        string shortName = what == "золота" ? "зол" : (what == "дерева" ? "дер" : (what == "камня" ? "кам" : "еды"));
        parts.Add(UiKit.Col(have >= need ? hex : UiKit.BadHex, need + " " + shortName));
    }

    static string MissingLine(BuildingType t)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return "";
        Balance.NextBuildCost(t, out int g, out int w, out int s, out int f);
        List<string> parts = new List<string>();
        if (rm.gold < g) parts.Add($"{g - rm.gold} золота");
        if (rm.wood < w) parts.Add($"{w - rm.wood} дерева");
        if (rm.stone < s) parts.Add($"{s - rm.stone} камня");
        if (rm.food < f) parts.Add($"{f - rm.food} еды");
        return string.Join(", ", parts);
    }

    string Describe(BuildingType t)
    {
        if (t.name == PopulationManager.HouseName && PopulationManager.Instance != null)
            return $"Жильё на {PopulationManager.Instance.HousePerLevel} чел.";
        if (t.name == BuildingInstance.GuestHouseName && PopulationManager.Instance != null)
            return $"Наёмникам: {PopulationManager.Instance.guestPerLevel} мест на уровень";
        if (ShortInfo.TryGetValue(t.name, out string s)) return s;

        if (t.prodWood > 0) return "Добывает дерево";
        if (t.prodStone > 0) return "Добывает камень";
        if (t.prodFood > 0) return "Даёт еду";
        if (t.prodGold > 0) return "Приносит золото";
        return "Постройка";
    }

    Vector3 MouseWorld()
    {
        Vector2 screen = Mouse.current.position.ReadValue();
        return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, Mathf.Abs(cam.transform.position.z)));
    }

    Vector2Int GetOriginUnderMouse(Vector2Int size)
    {
        Vector3 w = MouseWorld();
        return new Vector2Int(Mathf.RoundToInt(w.x - size.x / 2f), Mathf.RoundToInt(w.y - size.y / 2f));
    }

    List<Vector2Int> GetFootprint(Vector2Int origin, Vector2Int size)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                cells.Add(new Vector2Int(origin.x + x, origin.y + y));
        return cells;
    }

    bool CellsFree(List<Vector2Int> footprint)
    {
        int half = mapSize / 2;
        foreach (var c in footprint)
        {
            if (c.x < -half || c.x >= half || c.y < -half || c.y >= half) return false;
            if (occupied.ContainsKey(c)) return false;
        }
        return true;
    }

    bool ZoneOk(BuildingType t, List<Vector2Int> footprint)
    {
        if (t == null || Territory.Instance == null) return true;
        if (t.name == BuildingInstance.BridgeName) return true;   // мосту - свои правила (WaterProblem)
        return Territory.Instance.CanPlace(t.name, footprint);
    }

    // Край карты (final-6): несколько клеток у границы свободны всегда - там появляются войска.
    // Уже стоящие там здания из старых сохранений не трогаем - только новые постройки и перенос.
    static string EdgeProblem(List<Vector2Int> footprint)
    {
        Territory tr = Territory.Instance;
        if (tr == null) return "";
        foreach (var c in footprint)
            if (tr.InEdgeBand(c)) return "У края карты строить нельзя — здесь появляются войска";
        return "";
    }

    // Река: обычные здания на воду не ставят; мост - только на воду, от берега или от другой клетки моста.
    // "" - можно.
    string WaterProblem(BuildingType t, List<Vector2Int> footprint)
    {
        if (t == null) return "";
        bool bridge = t.name == BuildingInstance.BridgeName;
        if (!bridge)
        {
            // Обновление 12: стена и ворота - понятное объяснение (тестеры не знали, что на воду их не ставят)
            bool fence = t.name == BuildingInstance.WallName || t.name == BuildingInstance.GateName;
            foreach (var c in footprint)
            {
                if (fence && occupied.TryGetValue(c, out GameObject ob) && ob != null)
                {
                    BuildingInstance bi = ob.GetComponent<BuildingInstance>();
                    if (bi != null && bi.IsBridge) return "На мост стену и ворота не ставят — закройте стеной берег у моста";
                }
                if (Rivers.IsWater(c))
                    return fence ? "Стену и ворота на воду не ставят. Река сама задерживает врагов: вброд они идут втрое медленнее и в воде не дерутся. Стройте стену по берегу, башни — у воды"
                                 : "На реке строить нельзя — только мост";
            }
            return "";
        }

        Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        Territory tr = Territory.Instance;
        foreach (var c in footprint)
        {
            if (!Rivers.IsWater(c)) return "Мост ставят только на воду";
            if (Crossing.Occupies(c)) return "Здесь стоит переправа";
            bool fromBank = false, fromBridge = false;
            foreach (var d in dirs)
            {
                Vector2Int n = c + d;
                if (occupied.TryGetValue(n, out GameObject o) && o != null)
                {
                    BuildingInstance bi = o.GetComponent<BuildingInstance>();
                    if (bi != null && bi.IsBridge) { fromBridge = true; continue; }
                }
                if (!Rivers.IsWater(n) && (tr == null || tr.InZone(n))) fromBank = true;
            }
            if (!fromBank && !fromBridge) return "Мост тянут от берега в зелёной зоне или от другой клетки моста";
        }
        return "";
    }

    const int DoorOpenArea = 12;

    static bool NeedsDoor(BuildingType t)
    {
        if (t == null) return false;
        string n = t.name;
        if (BuildingInstance.IsRoadName(n) || n == BuildingInstance.WallName || n == BuildingInstance.GateName) return false;
        if (n == BuildingInstance.FarmName || n == BuildingInstance.BridgeName) return false;
        if (n == "Башня" || n == MagicCannonController.TypeName || n == GroundCannonController.TypeName || n == OutpostController.TypeName) return false;
        return true;
    }

    bool CellPassable(Vector2Int c, HashSet<Vector2Int> extra, GameObject ignore)
    {
        int half = mapSize / 2;
        if (c.x < -half || c.x >= half || c.y < -half || c.y >= half) return false;
        if (extra != null && extra.Contains(c)) return false;
        if (!occupied.TryGetValue(c, out GameObject o) || o == null || o == ignore) return !Rivers.IsWater(c);   // через воду к двери не пройти
        BuildingInstance bi = o.GetComponent<BuildingInstance>();
        return bi != null && bi.type != null && (bi.IsRoad || bi.IsWalkable || (bi.IsBarrier && bi.IsRuined));
    }

    bool OpenArea(Vector2Int start, HashSet<Vector2Int> extra, GameObject ignore)
    {
        var seen = new HashSet<Vector2Int> { start };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        while (queue.Count > 0)
        {
            if (seen.Count >= DoorOpenArea) return true;
            Vector2Int c = queue.Dequeue();
            foreach (var d in dirs)
            {
                Vector2Int n = c + d;
                if (seen.Contains(n) || !CellPassable(n, extra, ignore)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return seen.Count >= DoorOpenArea;
    }

    bool HasDoor(Vector2Int o, Vector2Int sz, HashSet<Vector2Int> extra, GameObject ignore)
    {
        for (int x = -1; x <= sz.x; x++)
            for (int y = -1; y <= sz.y; y++)
            {
                bool edgeX = (x == -1 || x == sz.x);
                bool edgeY = (y == -1 || y == sz.y);
                if (edgeX == edgeY) continue;
                Vector2Int c = new Vector2Int(o.x + x, o.y + y);
                if (CellPassable(c, extra, ignore) && OpenArea(c, extra, ignore)) return true;
            }
        return false;
    }

    static bool TouchesSide(BuildingInstance b, List<Vector2Int> cells)
    {
        Vector2Int o = b.OriginCell, sz = b.type.size;
        foreach (var c in cells)
        {
            bool inX = c.x >= o.x && c.x < o.x + sz.x;
            bool inY = c.y >= o.y && c.y < o.y + sz.y;
            if (inX && (c.y == o.y - 1 || c.y == o.y + sz.y)) return true;
            if (inY && (c.x == o.x - 1 || c.x == o.x + sz.x)) return true;
        }
        return false;
    }

    int DoorProblem(BuildingType t, Vector2Int origin, List<Vector2Int> footprint, GameObject ignore)
    {
        if (t == null || BuildingInstance.IsRoadName(t.name) || t.name == BuildingInstance.FarmName || t.name == BuildingInstance.BridgeName) return 0;
        var extra = new HashSet<Vector2Int>(footprint);

        if (NeedsDoor(t) && !HasDoor(origin, t.size, extra, ignore)) return 1;

        foreach (var inst in placedInstances)
        {
            if (inst == null || inst.type == null || !NeedsDoor(inst.type)) continue;
            if (ignore != null && inst.gameObject == ignore) continue;
            if (!TouchesSide(inst, footprint)) continue;
            if (HasDoor(inst.OriginCell, inst.type.size, null, ignore) && !HasDoor(inst.OriginCell, inst.type.size, extra, ignore)) return 2;
        }
        return 0;
    }

    // Все клетки заняты дорогой (не стройкой другого здания)
    bool AllRoad(List<Vector2Int> footprint)
    {
        int half = mapSize / 2;
        foreach (var c in footprint)
        {
            if (c.x < -half || c.x >= half || c.y < -half || c.y >= half) return false;
            if (!occupied.TryGetValue(c, out GameObject o) || o == null) return false;
            BuildingInstance bi = o.GetComponent<BuildingInstance>();
            if (bi == null || !bi.IsRoad) return false;
        }
        return true;
    }

    // Убрать дорогу под воротами: без возврата и без сообщения (дорога просто становится воротами)
    void RemoveRoadsQuiet(List<Vector2Int> footprint)
    {
        foreach (var c in footprint)
        {
            if (!occupied.TryGetValue(c, out GameObject o) || o == null) continue;
            BuildingInstance bi = o.GetComponent<BuildingInstance>();
            if (bi == null || !bi.IsRoad) continue;
            List<Vector2Int> cells = new List<Vector2Int>();
            foreach (var kv in occupied) if (kv.Value == o) cells.Add(kv.Key);
            foreach (var k in cells) occupied.Remove(k);
            placedInstances.Remove(bi);
            if (selectedInstance == bi) selectedInstance = null;
            Destroy(o);
        }
        Territory.MarkDirty();
        GridNavigation.MarkDirty();
    }

    bool CanBuildWhileMoving(List<Vector2Int> footprint, GameObject buildingToMove)
    {
        int half = mapSize / 2;
        foreach (var c in footprint)
        {
            if (c.x < -half || c.x >= half || c.y < -half || c.y >= half) return false;
            if (occupied.ContainsKey(c) && occupied[c] != buildingToMove) return false;
        }
        return true;
    }

    Vector3 CenterOf(Vector2Int origin, Vector2Int size)
    {
        return new Vector3(origin.x + size.x / 2f, origin.y + size.y / 2f, 0f);
    }

    void UpdateGhost(BuildingType type, Vector2Int origin, bool canBuild)
    {
        if (ghost == null) return;
        ghost.SetActive(true);
        SpriteRenderer sr = ghost.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            // Призрак показывает картинку здания, если она есть
            if (ghostPlainSprite == null) ghostPlainSprite = sr.sprite;
            Sprite art = GameArt.Building(type.name);
            Sprite want = art != null ? art : ghostPlainSprite;
            if (sr.sprite != want) sr.sprite = want;
        }
        FitToCells(ghost, CenterOf(origin, type.size), type.size);
        if (sr != null)
        {
            Color alphaMod = canBuild ? okColor : badColor;
            Color tc = GameArt.Tint(type);
            sr.color = new Color(tc.r * alphaMod.r, tc.g * alphaMod.g, tc.b * alphaMod.b, alphaMod.a);
        }
    }

    void PlaceBuilding(BuildingType type, Vector2Int origin, List<Vector2Int> footprint, bool deductCost)
    {
        GameObject prefab = type.prefab != null ? type.prefab : buildingPrefab;
        if (prefab == null) return;

        if (deductCost) CityIncome.Skip();
        if (deductCost && ResourceManager.Instance != null)
        {
            Balance.BuildCost(type, CountOfType(type.name), out int cg, out int cw, out int cs, out int cf);   // 2-е, 3-е здание дороже
            ResourceManager.Instance.Spend(cg, cw, cs, cf);
        }

        if (deductCost) UpgradeRules.PayBuild(type.name, CountOfType(type.name));

        GameObject b = Instantiate(prefab);
        b.name = type.name;
        GameArt.DressBuilding(b, type);   // нарисованная картинка вместо префаба (если есть)
        FitToCells(b, CenterOf(origin, type.size), type.size);

        SpriteRenderer sr = b.GetComponentInChildren<SpriteRenderer>();
        // Цвет ставим всегда: у общего префаба свой цвет (красный), он не должен проступать
        if (sr != null) sr.color = GameArt.Tint(type);
        if (sr != null && BuildingInstance.IsRoadName(type.name)) sr.sortingOrder = -10;
        if (sr != null && type.name == BuildingInstance.BridgeName) sr.sortingOrder = -9;   // над рекой, под людьми

        BuildingInstance inst = b.AddComponent<BuildingInstance>();
        inst.type = type;
        inst.OriginCell = origin;

        switch (type.name)
        {
            case "Ратуша": b.AddComponent<TownHallStorage>(); break;
            case BarracksController.TypeName: b.AddComponent<BarracksController>(); break;
            case "Мастерская": b.AddComponent<WorkshopController>(); break;
            case "Башня": b.AddComponent<TowerController>(); break;
            case MagicCannonController.TypeName: b.AddComponent<MagicCannonController>(); break;
            case HealerHouse.TypeName: b.AddComponent<HealerHouse>(); break;
            case BuildingInstance.GateName: b.AddComponent<GateController>(); b.AddComponent<WallJoin>(); break;
            case BuildingInstance.WallName: b.AddComponent<WallJoin>(); break;   // стена соединяется с соседями во все стороны
            case BuildingInstance.RoadName:
            case BuildingInstance.CobbleName: b.AddComponent<RoadJoin>(); break;   // дорога соединяется с соседями и мягко уходит в траву
            case CourierHouse.TypeName: b.AddComponent<CourierHouse>(); break;
            case WarehouseController.TypeName: b.AddComponent<WarehouseController>(); break;
            case ArmoryController.TypeName: b.AddComponent<ArmoryController>(); break;
            case StableController.TypeName: b.AddComponent<StableController>(); break;
            case OutpostController.TypeName: b.AddComponent<OutpostController>(); break;
            case BerryStation.TypeName: b.AddComponent<BerryStation>(); break;
            case KitchenController.TypeName: b.AddComponent<KitchenController>(); break;
            case WagonYard.TypeName: b.AddComponent<WagonYard>(); break;
            case PotionHouse.TypeName: b.AddComponent<PotionHouse>(); break;
            case WeaponForge.TypeName: b.AddComponent<WeaponForge>(); break;
            case FoundryController.TypeName: b.AddComponent<FoundryController>(); break;
            case EngineerYard.TypeName: b.AddComponent<EngineerYard>(); break;
            case MarketHouse.TypeName: b.AddComponent<MarketHouse>(); break;
            case BuildingInstance.BridgeName: b.AddComponent<BridgeController>(); b.AddComponent<BridgeLook>(); break;   // пролёт поворачивается по реке (final-7)
            case GroundCannonController.TypeName: b.AddComponent<GroundCannonController>(); break;
            case MagicArmory.TypeName: b.AddComponent<MagicArmory>(); break;
            case LifeAltar.TypeName: b.AddComponent<LifeAltar>(); break;
            case EngineerBarracks.TypeName: b.AddComponent<EngineerBarracks>(); break;   // сама добавит казарму в режиме инженеров
            case IndustrialPlant.TypeName: b.AddComponent<IndustrialPlant>(); break;
            case WarBuilderYard.TypeName: b.AddComponent<WarBuilderYard>(); break;
        }

        foreach (var c in footprint) occupied[c] = b;
        placedInstances.Add(inst);
        Territory.MarkDirty();

        // Новое здание сначала строится (время - Balance); стартовые и из сохранения стоят сразу
        if (deductCost) inst.StartConstruction(Balance.BuildSeconds(type, Mathf.Max(0, CountOfType(type.name) - 1)));   // само здание уже в списке
    }

    void StartMoveBuildingUnderMouse()
    {
        if (BuildBlocked)
        {
            WarManager.Notify("Идёт война — переносить здания нельзя", false);
            return;
        }

        Vector3 w = MouseWorld();
        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.y));
        if (!occupied.TryGetValue(cell, out GameObject b)) return;
        StartMove(b);
    }

    /// <summary>Взять здание в руки для переноса (клавиша M, меню долгого нажатия, кнопка в панели здания).</summary>
    void StartMove(GameObject b)
    {
        if (b == null) return;
        if (BuildBlocked)
        {
            WarManager.Notify("Идёт война — переносить здания нельзя", false);
            return;
        }
        BuildingInstance inst = b.GetComponent<BuildingInstance>();
        if (inst == null) return;

        if (inst.IsTownHall)
        {
            WarManager.Notify("Цитадель переносить нельзя", false);
            return;
        }
        if (inst.IsBridge)
        {
            WarManager.Notify("Мост не переносят — снесите клетку и постройте заново", false);
            return;
        }

        isMoving = true;
        movingBuilding = b;
        movingBuildingType = inst.type;
        movingBuilding.SetActive(false);
        selectedInstance = null;
        if (shopOpen) SetShopOpen(false);
    }

    void ConfirmMove(Vector2Int newOrigin, List<Vector2Int> newFootprint)
    {
        List<Vector2Int> keysToRemove = new List<Vector2Int>();
        foreach (var kv in occupied)
        {
            if (kv.Value == movingBuilding) keysToRemove.Add(kv.Key);
        }
        foreach (var c in keysToRemove) occupied.Remove(c);

        movingBuilding.SetActive(true);
        FitToCells(movingBuilding, CenterOf(newOrigin, movingBuildingType.size), movingBuildingType.size);

        BuildingInstance movedInst = movingBuilding.GetComponent<BuildingInstance>();
        if (movedInst != null) movedInst.OriginCell = newOrigin;

        foreach (var c in newFootprint) occupied[c] = movingBuilding;

        isMoving = false;
        movingBuilding = null;
        movingBuildingType = null;
    }

    void CancelMove()
    {
        if (movingBuilding != null)
        {
            movingBuilding.SetActive(true);
        }
        isMoving = false;
        movingBuilding = null;
        movingBuildingType = null;
    }

    // ---------- Снос ----------
    public const float DemolishRefund = 0.5f;        // какая доля цены постройки возвращается в Цитадель
    public const float DemolishRuinRefund = 0.25f;   // если сносят руины

    // X: спросить, точно ли сносить здание под курсором
    void AskDemolishUnderMouse()
    {
        Vector3 w = MouseWorld();
        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.y));

        if (!occupied.TryGetValue(cell, out GameObject b) || b == null) return;
        AskDemolish(b);
    }

    /// <summary>Спросить, точно ли сносить здание (клавиша X, меню долгого нажатия, кнопка в панели здания).</summary>
    void AskDemolish(GameObject b)
    {
        if (b == null) return;
        if (BuildBlocked)
        {
            WarManager.Notify("Идёт война — сносить здания нельзя", false);
            return;
        }
        if (b.name == townHall.name)
        {
            WarManager.Notify("Цитадель снести нельзя", false);
            return;
        }
        if (starterBuildings.Contains(b))
        {
            WarManager.Notify("Стартовые постройки снести нельзя (их можно двигать и улучшать)", false);
            return;
        }

        BuildingInstance inst = b.GetComponent<BuildingInstance>();
        string name = b.name;
        int lvl = inst != null ? inst.level : 1;
        string caption = lvl > 1 ? $"Снести «{name}» · Ур. {lvl}?" : $"Снести «{name}»?";
        DemolishDialog.Ask(b, caption, DescribeRefund(inst), () => Demolish(b));
    }

    // Что вернётся в Цитадель при сносе: доля цены постройки (и досок/плит, если они были нужны)
    void RefundFor(BuildingInstance inst, out int g, out int w, out int s, out int f, out Refined[] extra, out QualityTier tier)
    {
        g = w = s = f = 0;
        extra = null;
        tier = QualityTier.Medium;
        if (inst == null || inst.type == null) return;

        BuildingType t = inst.type;
        // Стройку отменяют - вся цена назад; руины - четверть; целое здание - половина
        float k = inst.UnderConstruction ? 1f : (inst.IsRuined ? DemolishRuinRefund : DemolishRefund);
        // Цена последнего по счёту. Готовое здание - от меньшей из новой и старой цены (дома, построенные до final-8)
        int pg, pw, ps, pf;
        if (inst.UnderConstruction) Balance.BuildCost(t, Mathf.Max(0, CountOfType(t.name) - 1), out pg, out pw, out ps, out pf);
        else Balance.RefundBase(t, Mathf.Max(0, CountOfType(t.name) - 1), out pg, out pw, out ps, out pf);
        g = Mathf.FloorToInt(pg * k);
        w = Mathf.FloorToInt(pw * k);
        s = Mathf.FloorToInt(ps * k);
        f = Mathf.FloorToInt(pf * k);

        // Доски/плиты за постройку зависят от номера здания; сносим последнее по счёту
        Refined[] paid = UpgradeRules.BuildExtra(t.name, Mathf.Max(0, CountOfType(t.name) - 1), out tier);
        if (paid == null) return;
        List<Refined> back = new List<Refined>();
        foreach (var it in paid)
        {
            int n = Mathf.FloorToInt(it.amount * k);
            if (n > 0) back.Add(new Refined { kind = it.kind, amount = n });
        }
        if (back.Count > 0) extra = back.ToArray();
    }

    string DescribeRefund(BuildingInstance inst)
    {
        RefundFor(inst, out int g, out int w, out int s, out int f, out Refined[] extra, out _);
        string pct = inst != null && inst.UnderConstruction ? "вся цена (стройка ещё не закончена)"
                   : (inst != null && inst.IsRuined ? "четверть цены (это руины)" : "половина цены постройки");
        string text = "Вернётся в Цитадель " + pct + ":\n" + UiKit.Cost(g, w, s, f);
        if (extra != null)
        {
            List<string> parts = new List<string>();
            foreach (var it in extra) parts.Add(UpgradeRules.KindLabel(it.kind) + " " + it.amount);
            text += UiKit.Col(UiKit.MutedHex, " · ") + string.Join(", ", parts);
        }
        if (inst != null && !inst.IsRuined && inst.StoredTotal > 0)
            text += "\nИ всё, что лежит в здании: " + inst.StoredTotal + " ед.";
        text += "\n" + UiKit.Col(UiKit.MutedHex, "Отменить снос нельзя.");
        return text;
    }

    // Снос после подтверждения
    void Demolish(GameObject b)
    {
        if (b == null) return;
        if (BuildBlocked)
        {
            WarManager.Notify("Идёт война — сносить здания нельзя", false);
            return;
        }

        BuildingInstance inst = b.GetComponent<BuildingInstance>();
        Sfx.At("demolish", inst != null ? inst.Center : (Vector2)b.transform.position);

        // Возврат в Цитадель: часть цены и то, что лежало на складе здания
        if (inst != null)
        {
            RefundFor(inst, out int g, out int w, out int s, out int f, out Refined[] extra, out QualityTier tier);
            if (ResourceManager.Instance != null) ResourceManager.Instance.Add(g, w, s, f);
            if (extra != null && TownHallStorage.Instance != null)
                foreach (var it in extra) TownHallStorage.Instance.Add(it.kind, tier, it.amount);
            if (!inst.IsRuined) inst.CollectToTreasury();
            List<string> got = new List<string>();
            if (g > 0) got.Add(g + " зол");
            if (w > 0) got.Add(w + " дер");
            if (s > 0) got.Add(s + " кам");
            if (f > 0) got.Add(f + " еды");
            if (extra != null) foreach (var it in extra) got.Add(UpgradeRules.KindLabel(it.kind) + " " + it.amount);
            WarManager.Notify(got.Count > 0 ? $"«{b.name}» снесено. В Цитадель вернулось: {string.Join(", ", got)}" : $"«{b.name}» снесено", true);
        }

        List<Vector2Int> toFree = new List<Vector2Int>();
        foreach (var kv in occupied)
            if (kv.Value == b) toFree.Add(kv.Key);
        foreach (var c in toFree) occupied.Remove(c);

        if (inst != null)
        {
            if (selectedInstance == inst) selectedInstance = null;
            placedInstances.Remove(inst);
        }

        Destroy(b);
        Territory.MarkDirty();
    }

    void FitToCells(GameObject obj, Vector3 center, Vector2Int size)
    {
        SpriteRenderer sr = obj.GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null) { obj.transform.position = center; return; }

        obj.transform.rotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;
        obj.transform.position = Vector3.zero;

        Vector3 s = sr.bounds.size;
        if (s.x <= 0.0001f || s.y <= 0.0001f) return;

        obj.transform.localScale = new Vector3(size.x / s.x, size.y / s.y, 1f);
        Vector3 offset = center - sr.bounds.center;
        obj.transform.position = new Vector3(offset.x, offset.y, 0f);
    }

    // Закреплённая подсказка по управлению (кнопка «?» рядом с «Постройки»)
    private bool showControls = false;

    const string ControlsTip =
        "<b>Управление</b>\n" +
        "ЛКМ — выбрать здание, жителя или врага\n" +
        "M — перенести здание под курсором\n" +
        "X — снести здание под курсором (с вопросом; в войну нельзя)\n" +
        "1–9 — взять постройку в руки\n" +
        "ПКМ — камера, в режиме стройки — отмена\n" +
        "Esc — отмена";

    void OnGUI()
    {
        if (WarView.IsOpen) return;   // открыта карта клановой войны
        UiKit.Apply();
        if (buildingTypes == null || buildingTypes.Length == 0 || !worldReady) return;
        if (RaidManager.ViewingBattle) return;

        if (shopOpen && UiKit.Behind("shop")) shopOpen = false;   // открыли другое большое окно

        // Поверх открыто другое окно (путь, поход, клан, рынок...) - кнопки слева сверху не рисуем:
        // иначе они вылезают сквозь окно и нажимаются вместо него
        leftHidden = !shopOpen && !isBuildingMode && !isMoving
            && OtherWindowOver(new Rect(buildButtonRect.x, buildButtonRect.y, buildButtonRect.width, buildButtonRect.height + 76f));

        if (!leftHidden)
        {
            // Кнопка «Постройки» и «?» рядом (обе внутри buildButtonRect - мышь над ними не кликает по карте)
            bool war = BuildBlocked;
            Rect mainBtn = new Rect(buildButtonRect.x, buildButtonRect.y, buildButtonRect.width - 44f, buildButtonRect.height);
            Rect helpBtn = new Rect(WarCityMode.Active ? buildButtonRect.x : mainBtn.xMax + 4f, buildButtonRect.y, 40f, buildButtonRect.height);   // klan-voina-14

            UiKit.Kind kind = war ? UiKit.Kind.Danger : ((shopOpen || isBuildingMode) ? UiKit.Kind.Primary : UiKit.Kind.Normal);
            GUIStyle bigBtn = new GUIStyle(UiKit.ButtonStyle(kind)) { fontSize = 15, fontStyle = FontStyle.Bold };
            string btnText = war ? "Строить нельзя" : (shopOpen ? "Закрыть" : "Постройки");
            // klan-voina-14: в военной деревне строить нельзя никогда - большую красную кнопку не рисуем (там места мало)
            if (!WarCityMode.Active)
            {
                if (LocGUI.Button(mainBtn, btnText, bigBtn) && !isMoving)
                    SetShopOpen(!shopOpen);
                UiKit.Tip(mainBtn, war ? "Идёт война — строить и переносить здания нельзя" : "Окно построек (Esc — закрыть)");
            }

            // На телефоне «?» открывает подсказку по жестам (про мышь и клавиши там не нужно)
            if (UiKit.Phone)
            {
                if (UiKit.Button(helpBtn, "?", UiKit.Kind.Ghost, "Как управлять пальцами")) TouchControls.ShowHelp();
                showControls = false;
            }
            else if (UiKit.Button(helpBtn, "?", showControls ? UiKit.Kind.Primary : UiKit.Kind.Ghost, showControls ? null : ControlsTip))
                showControls = !showControls;

            float underY = buildButtonRect.yMax + 6f;
            // Магазин марок - кнопкой под «Постройками», под ним - щит города (если он есть)
            if (!isMoving && !shopOpen) underY = StampShop.DrawButton(new Rect(buildButtonRect.x, underY, buildButtonRect.width, 30f)) + 6f;
            if (!isMoving && !shopOpen) underY = CityShield.DrawChip(new Rect(buildButtonRect.x, underY, buildButtonRect.width, 26f)) + 6f;
            if (!isMoving && !shopOpen) underY = ReplayViewer.DrawChip(new Rect(buildButtonRect.x, underY, buildButtonRect.width, 26f)) + 6f;   // обновление 13
            if (isMoving) DrawMoveHint(underY);
            else if (showControls && !isBuildingMode) DrawControlsBox(underY);
        }

        if (shopOpen)
        {
            DrawShop();
            return;
        }
        if (isBuildingMode && !isMoving) DrawCursorTip();

        if (selectedEnemy != null && EnemyAgent.Alive(selectedEnemy) && !isBuildingMode && !isMoving)
        {
            EnemyInfoPanel.DrawMarker(selectedEnemy);
            infoPanelRect = DrawScrolled(selectedEnemy, 265f, 45f, () => EnemyInfoPanel.Draw(selectedEnemy, 0f, 0f, 330f));
        }

        if (selectedGolem != null && selectedGolem.isActiveAndEnabled && !isBuildingMode && !isMoving)
        {
            GolemInfoPanel.DrawMarker(selectedGolem);
            infoPanelRect = DrawScrolled(selectedGolem, 265f, 45f, () => GolemInfoPanel.Draw(selectedGolem, 0f, 0f, 330f));
        }

        if (selectedCitizen == null && commandOnly != null && !isBuildingMode && !isMoving) DrawCommandStrip();
        if (selectedCitizen != null && !isBuildingMode && !isMoving)
        {
            CitizenInfoPanel.DrawMarker(selectedCitizen);
            infoPanelRect = DrawScrolled(selectedCitizen, 265f, 45f, () => CitizenInfoPanel.Draw(selectedCitizen, 0f, 0f, 330f));
        }

        if (selectedCamp != null && !isBuildingMode && !isMoving)
        {
            infoPanelRect = DrawScrolled(selectedCamp, 265f, 45f, () => selectedCamp.DrawPanel(0f, 0f, 310f));
        }

        if (selectedInstance != null && selectedInstance.gameObject != null)
        {
            if (!isBuildingMode && !isMoving) DrawSelectionFrame(selectedInstance);
            infoPanelRect = DrawScrolled(selectedInstance, 265f, 45f, DrawBuildingPanelWithEconomy);

            BarracksController bm = selectedInstance.GetComponent<BarracksController>();
            if (bm != null) bm.DrawMarkers();

            DrawDurability(selectedInstance);
            DrawBuildingActions(selectedInstance);
        }
        else actionsRect = new Rect(0f, 0f, 0f, 0f);
    }

    // Режим переноса: подсказка видна всё время, пока здание «в руках»
    void DrawMoveHint(float y)
    {
        string door = DoorHint();
        float h = door != "" ? 70f : 52f;
        Rect r = new Rect(buildButtonRect.x, y, buildButtonRect.width, h);
        LocGUI.Box(r, GUIContent.none);
        LocGUI.Label(new Rect(r.x + 10f, r.y + 5f, r.width - 20f, 20f), UiKit.Col(UiKit.AccentHex, UiKit.B("Перенос здания")));
        LocGUI.Label(new Rect(r.x + 10f, r.y + 26f, r.width - 20f, 20f),
            UiKit.Phone ? "Ведите пальцем · «✓ Поставить»" : "ЛКМ — поставить   Esc — отмена", UiKit.MutedStyle);
        if (door != "") LocGUI.Label(new Rect(r.x + 10f, r.y + 46f, r.width - 20f, 20f), door.TrimStart('\n'), UiKit.Small);
    }

    void DrawControlsBox(float y)
    {
        float h = UiKit.TextH(ControlsTip, buildButtonRect.width - 20f, UiKit.Small) + 12f;
        Rect r = new Rect(buildButtonRect.x, y, buildButtonRect.width, h);
        LocGUI.Box(r, GUIContent.none);
        LocGUI.Label(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, h - 10f), ControlsTip, UiKit.Small);
    }

    Vector2 panelScroll;
    object panelKey;
    float panelContentW = 330f, panelContentH = 300f;
    // Снизу под панелью - плашка прочности и кнопки «Перенести» / «Снести» (часы и мана теперь наверху)
    // На телефоне ниже 56 точек от низа - кнопки «В бой!», «Переправа», разведчики
    float PanelBottomMargin => WarView.Embedded
        ? WarView.PanelBottomReserve + UnderPanelH() + 8f   // klan-voina-14: на войне окно и плашка прочности - над полосами отрядов и приказов
        : UiKit.Phone ? Mathf.Max(56f, UnderPanelH() + 8f) : Mathf.Max(130f, UnderPanelH() + 8f);
    const float PanelBarW = 16f;

    /// <summary>Закрыть окно выбранного (здание, житель, лагерь, враг, голем).
    /// Окно бойца с включённым «Послать отряд» сворачивается в плашку: отряд по-прежнему слушает нажатия (final-9).</summary>
    void CloseSelection(bool keepCommand = true)
    {
        if (keepCommand && selectedCitizen != null && CitizenInfoPanel.OrderArmed && CitizenInfoPanel.Orderable(selectedCitizen))
            commandOnly = selectedCitizen;
        else commandOnly = null;
        selectedInstance = null; selectedCamp = null; selectedCitizen = null; selectedEnemy = null; selectedGolem = null;
        panelKey = null;
        actionsRect = durabilityRect = new Rect(0f, 0f, 0f, 0f);
        Sfx.Ui("ui_click");
    }

    // Свёрнутое окно бойца (final-9): отряд слушает нажатия по карте. «Открыть» - снова карточка, «✕» - отпустить.
    Rect commandStripRect;

    void DrawCommandStrip()
    {
        if (!CitizenInfoPanel.Orderable(commandOnly) || !CitizenInfoPanel.OrderArmed) { commandOnly = null; commandStripRect = default; return; }
        CitizenInfoPanel.DrawMarker(commandOnly);
        float w = Mathf.Min(360f, UiKit.W - 280f), h = UiKit.Phone ? 40f : 34f;
        Rect r = new Rect(265f, 45f, Mathf.Max(220f, w), h);
        commandStripRect = r;
        infoPanelRect = r;   // мышь над плашкой - не в карту
        LocGUI.Box(r, GUIContent.none);
        float bw = UiKit.Phone ? 40f : 30f;
        string who = commandOnly.Squad != null ? $"отряд ({SquadCount(commandOnly.Squad)})" : "отряд";
        LocGUI.Label(new Rect(r.x + 8f, r.y, r.width - bw * 2f - 20f, r.height),
            UiKit.Col(UiKit.InfoHex, "➜ " + who) + UiKit.Col(UiKit.MutedHex, UiKit.Phone ? " — нажмите, куда идти (дважды — марш)" : " — щёлкните, куда идти (дважды — марш)"),
            new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = true });
        if (UiKit.Button(new Rect(r.xMax - bw * 2f - 8f, r.y + 4f, bw, r.height - 8f), "☰", UiKit.Kind.Ghost, "Открыть карточку бойца"))
        {
            selectedCitizen = commandOnly;
            commandOnly = null;
        }
        if (UiKit.Button(new Rect(r.xMax - bw - 4f, r.y + 4f, bw, r.height - 8f), "✕", UiKit.Kind.Ghost, "Отпустить отряд: нажатия по карте снова выбирают"))
            ReleaseCommand();
    }

    static int SquadCount(Squad sq)
    {
        int n = 0;
        foreach (var m in sq.Members) if (m != null && !m.IsDead) n++;
        return n;
    }

    void ReleaseCommand()
    {
        commandOnly = null;
        CitizenInfoPanel.OrderArmed = false;
        commandStripRect = default;
        Sfx.Ui("ui_click");
    }

    // Рамка вокруг выбранного здания на карте (final-6): видно, чьё окно открыто.
    // Раньше две мастерские давали одинаковые окна - казалось, что щелчок по второй не сработал.
    BuildingInstance frameOf;
    Vector2Int frameMin, frameMax;
    int frameFrame = -100;

    void DrawSelectionFrame(BuildingInstance b)
    {
        if (b == null || cam == null || Event.current.type != EventType.Repaint) return;
        if (!ReferenceEquals(frameOf, b) || Time.frameCount - frameFrame > 30)   // здание могли перенести - пересчёт раз в полсекунды
        {
            frameOf = b; frameFrame = Time.frameCount;
            bool any = false;
            foreach (var kv in occupied)
            {
                if (kv.Value != b.gameObject) continue;
                if (!any) { frameMin = frameMax = kv.Key; any = true; }
                else { frameMin = Vector2Int.Min(frameMin, kv.Key); frameMax = Vector2Int.Max(frameMax, kv.Key); }
            }
            if (!any) { frameMin = frameMax = b.OriginCell; }
        }
        Vector2 a = UiKit.ToGui(cam.WorldToScreenPoint(new Vector3(frameMin.x, frameMin.y, 0f)));
        Vector2 c = UiKit.ToGui(cam.WorldToScreenPoint(new Vector3(frameMax.x + 1f, frameMax.y + 1f, 0f)));
        Rect r = Rect.MinMaxRect(Mathf.Min(a.x, c.x) - 2f, Mathf.Min(a.y, c.y) - 2f, Mathf.Max(a.x, c.x) + 2f, Mathf.Max(a.y, c.y) + 2f);
        if (r.width < 4f || r.width > UiKit.W * 2f) return;
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
        Color old = GUI.color;
        const float t = 2f;
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        DrawFrame(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), t + 2f);
        GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, pulse);
        DrawFrame(r, t);
        GUI.color = old;
    }

    static void DrawFrame(Rect r, float t)
    {
        Texture2D w = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), w);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), w);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), w);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), w);
    }

    Rect DrawScrolled(object key, float x, float y, System.Func<Rect> draw)
    {
        if (!ReferenceEquals(panelKey, key)) { panelKey = key; panelScroll = Vector2.zero; }

        float maxH = Mathf.Max(120f, UiKit.H - y - PanelBottomMargin);
        bool need = panelContentH > maxH;
        float viewH = Mathf.Min(panelContentH, maxH);
        float viewW = panelContentW + (need ? PanelBarW : 0f);
        Rect view = new Rect(x, y, viewW, viewH);
        if (need && view.Contains(Event.current.mousePosition)) CameraController.BlockZoom();   // колёсико крутит панель, а не карту

        panelScroll = UiKit.BeginScrollView(view, panelScroll, new Rect(0f, 0f, panelContentW, panelContentH),
            GUIStyle.none, GUI.skin.verticalScrollbar);
        TouchControls.ClipBegin(view);
        Rect r;
        try { r = draw(); }
        finally { TouchControls.ClipEnd(); }
        GUI.EndScrollView();

        panelContentW = Mathf.Max(100f, r.xMax);
        panelContentH = Mathf.Max(40f, r.yMax);
        // Крестик в шапке (final-6): окно закрывается одним нажатием, не надо искать пустое место на карте
        if (UiKit.CloseButton(new Rect(x, y, Mathf.Min(panelContentW, viewW), UiKit.TitleH))) CloseSelection();
        return view;
    }

    // ---------- Панели зданий ----------

    const float PanelTop = UiKit.TitleH + 6f;   // где начинается содержимое под шапкой окна
    const float WorkersRowH = 30f;               // строка «- Рабочие + Авто» (PopulationManager.DrawWorkersRow)

    // Окно панели с шапкой «Название · Ур. N»
    static void PanelWindow(float x, float y, float w, float h, string name, int lvl)
    {
        UiKit.Window(new Rect(x, y, w, h), lvl > 0 ? $"{name}  {UiKit.Col(UiKit.MutedHex, "· Ур. " + lvl)}" : name);
    }

    // Высота строки состояния «что не так» (пусто - 0)
    static float StatusH(string text, float w)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        return UiKit.TextH(text, w, UiKit.Small) + 4f;
    }

    // Здание работает, но сейчас ночь или выходной: на месте только дежурные (цвет свой - это не поломка)
    static string ShiftStatus(BuildingInstance b)
    {
        GameClock clock = GameClock.Instance;
        if (b == null || b.workers <= 0) return "";
        int on = Mathf.Min(b.workers, b.workersOnSite);
        if (on >= b.workers) return "";
        if (clock == null || clock.IsWorkTime)
            return UiKit.Col(UiKit.MutedHex, $"Рабочие идут на место: дошли {on} из {b.workers}. Работают только дошедшие.");
        string when = clock.IsWeekend ? "Выходной" : "Ночь";
        return UiKit.Col(UiKit.WarnHex, $"{when}: на месте только дежурные ({on} из {b.workers}). " +
            (clock.SecondsToWork > clock.nightMinutes * 60f + 1f ? "Все вернутся в понедельник утром." : "Утром все вернутся."));
    }

    static void DrawStatus(float x, float y, float w, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        LocGUI.Label(new Rect(x, y, w, UiKit.TextH(text, w, UiKit.Small)), UiKit.Col(UiKit.BadHex, text), UiKit.Small);
    }

    static string Stored(string what, int n, string hex)
    {
        return n > 0 ? UiKit.Col(hex, $"{what} {n}") : null;
    }

    // Цитадель: заполнение казны (золото, дерево, камень, еда) и лимиты материалов
    static void DrawHallLimits(float x, float y, float w, int lvl)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;
        StoreRes[] main = { StoreRes.Gold, StoreRes.Wood, StoreRes.Stone, StoreRes.Food };
        float half = (w - 6f) * 0.5f;
        for (int i = 0; i < main.Length; i++)
        {
            int v = rm.Get(main[i]), cap = StorageLimits.Cap(main[i]);
            float frac = cap > 0 ? Mathf.Clamp01(v / (float)cap) : 0f;
            Rect r = new Rect(x + (i % 2) * (half + 6f), y + (i / 2) * 22f, half, 18f);
            UiKit.Bar(r, frac, frac >= 1f ? UiKit.Bad : (frac > 0.85f ? UiKit.Accent : UiKit.Good), $"{StorageLimits.Name(main[i])}: {v}/{cap}");
        }
        int wh = 0;
        foreach (var c in WarehouseController.All) if (c != null && c.Building != null && !c.Building.UnderConstruction) wh++;
        Rect line = new Rect(x, y + 44f, w, 18f);
        LocGUI.Label(line, UiKit.Col(UiKit.MutedHex,
            $"Доски и плиты до {StorageLimits.RefinedCapOf(RefinedKind.Planks)}, сталь до {StorageLimits.RefinedCapOf(RefinedKind.Steel)} · Хранилищ: {wh}"), UiKit.Small);
        UiKit.Tip(new Rect(x, y, w, 62f), HallLimitsTip(lvl));
    }

    static string HallLimitsTip(int lvl)
    {
        int lv = Mathf.Clamp(lvl, 1, 3);
        bool next = lv < 3;
        var sb = new System.Text.StringBuilder();
        sb.Append("Сколько всего может лежать в городе. Сверх лимита ничего не копится: курьеры оставляют добычу в зданиях, ");
        sb.Append("мастерская не берёт заказ, рынок не продаёт. Лимит растёт с уровнем Цитадели, каждое Хранилище добавляет ещё.\n");
        for (int i = 0; i < StorageLimits.ResCount; i++)
        {
            StoreRes r = (StoreRes)i;
            if (!StorageLimits.Limited(r)) continue;
            int hall = StorageLimits.HallCapAt(r, lv), wh = StorageLimits.WarehousesBonus(r);
            string whText = wh > 0 ? $" + {wh} (Хранилища)" : "";
            string nextText = next ? $"  → Ур. {lv + 1}: {StorageLimits.HallCapAt(r, lv + 1)}" : "";
            sb.Append($" • {StorageLimits.Name(r)}: {hall}{whText}{nextText}\n");
        }
        sb.Append("Материалы в Цитадели (все качества вместе), сверх них — в Хранилищах:\n");
        foreach (RefinedKind k in System.Enum.GetValues(typeof(RefinedKind)))
        {
            string nextText = next ? $"  → Ур. {lv + 1}: {StorageLimits.RefinedCapAt(k, lv + 1)}" : "";
            sb.Append($" • {UpgradeRules.KindLabel(k)}: {StorageLimits.RefinedCapAt(k, lv)}{nextText}\n");
        }
        return sb.ToString().TrimEnd();
    }

    // Панель здания + под ней «Экономика здания» (что даёт, что тратит, чего не хватает городу - CityStats)
    Rect DrawBuildingPanelWithEconomy()
    {
        if (selectedInstance.UnderConstruction) return DrawConstructionPanel();
        Rect r = DrawBuildingPanel();
        float w = Mathf.Max(260f, r.width - 5f);
        float h = CityStats.BuildingBoxHeight(selectedInstance, w);
        if (h <= 0f) return r;
        CityStats.DrawBuildingBox(new Rect(r.x, r.yMax + 4f, w, h), selectedInstance);
        return new Rect(r.x, r.y, r.width, r.height + 4f + h);
    }

    // Стройка: сколько осталось, подсказка про снос
    Rect DrawConstructionPanel()
    {
        const float w = 310f, cx = UiKit.Pad, cw = w - UiKit.Pad * 2f;
        float h = PanelTop + 50f + 6f + 40f + 8f;
        PanelWindow(0f, 0f, w, h, selectedInstance.type != null ? selectedInstance.type.name : "Стройка", 0);
        UpgradeRules.DrawTimer(cx, PanelTop, cw, selectedInstance);
        LocGUI.Label(new Rect(cx, PanelTop + 56f, cw, 40f),
            UiKit.Col(UiKit.MutedHex, "Пока здание строится, оно не работает. Снести стройку (X) — вернётся вся цена."), UiKit.Small);
        return new Rect(0f, 0f, w + 5f, h);
    }

    Rect DrawBuildingPanel()
    {
        const float infoX = 0f, infoY = 0f, infoWidth = 310f;
        Rect empty = new Rect(0f, 0f, infoWidth + 5f, 40f);
        const float cx = infoX + UiKit.Pad, cw = infoWidth - UiKit.Pad * 2f;

        string bName = selectedInstance.type != null ? selectedInstance.type.name : "Ратуша";
        int lvl = selectedInstance.level;

        if (bName == "Ратуша")
        {
            // Склад - плитками с картинками (ItemIcons); пусто - строка подсказки
            string storageText = "";
            var stored = TownHallStorage.Instance != null ? TownHallStorage.Instance.BuildEntries() : new List<ItemIcons.Entry>();
            if (TownHallStorage.Instance == null) storageText = UiKit.Col(UiKit.MutedHex, "Склад недоступен");
            else if (stored.Count == 0) storageText = UiKit.Col(UiKit.MutedHex, "Пусто — доски, плиты и сталь делают в Мастерской");

            int outposts = Territory.Instance != null ? Territory.Instance.ActiveOutposts : 0;
            int idle = Territory.Instance != null ? Territory.Instance.IdleOutposts : 0;

            float storeH = (stored.Count > 0 ? ItemIcons.GridHeight(stored.Count, cw) : UiKit.TextH(storageText, cw, UiKit.Small)) + 4f;
            float zoneY = PanelTop;
            float workersY = zoneY + UiKit.RowH * 2f + (idle > 0 ? UiKit.RowH : 0f) + 4f;
            float storeY = workersY + WorkersRowH;
            // Вместимость (лимиты StorageLimits): 4 полосы казны и строка про материалы
            bool limits = StorageLimits.Active;
            float limY = storeY + UiKit.SectionH + storeH + 4f;
            float limH = limits ? 2f * 22f + 20f : 0f;
            float upY = limits ? limY + UiKit.SectionH + limH + 4f : limY;
            float h = upY + UiKit.SectionH + UpgradeRules.SectionHeight(selectedInstance) + 8f;

            PanelWindow(infoX, infoY, infoWidth, h, "Ратуша", lvl);

            // Главное: зона застройки и аванпосты
            string next = lvl < UpgradeRules.MaxLevelFor(selectedInstance)
                ? UiKit.Col(UiKit.MutedHex, $"  → {Territory.HallRadiusFor(lvl + 1):F0} на Ур. {lvl + 1}") : "";
            Rect zr = new Rect(cx, infoY + zoneY, cw, UiKit.RowH);
            UiKit.Row(zr, "Зона застройки", $"{Territory.HallRadiusFor(lvl):F0} кл.{next}");
            UiKit.Tip(zr, "Радиус зелёной зоны вокруг Цитадели: строить можно только в ней. Растёт с уровнем Цитадели и аванпостами.");
            UiKit.Row(new Rect(cx, infoY + zoneY + UiKit.RowH, cw, UiKit.RowH), "Аванпостов", outposts.ToString());
            if (idle > 0)
            {
                Rect ir = new Rect(cx, infoY + zoneY + UiKit.RowH * 2f, cw, UiKit.RowH);
                LocGUI.Label(ir, UiKit.Col(UiKit.WarnHex, $"Не работают: {idle} (нет дороги или снабжения)"), UiKit.Small);
            }

            if (PopulationManager.Instance != null)
                PopulationManager.Instance.DrawWorkersRow(cx, infoY + workersY, selectedInstance);

            float sy = UiKit.Section(cx, infoY + storeY, cw, "Склад материалов");
            if (stored.Count > 0) ItemIcons.DrawGrid(cx, sy, cw, stored);
            else LocGUI.Label(new Rect(cx, sy, cw, storeH), storageText, UiKit.Small);

            if (limits) DrawHallLimits(cx, UiKit.Section(cx, infoY + limY, cw, "Вместимость"), cw, lvl);

            float uy = UiKit.Section(cx, infoY + upY, cw, "Улучшение");
            UpgradeRules.DrawUpgradeSection(cx, uy, cw, selectedInstance);
            return new Rect(infoX, infoY, infoWidth + 5, h);
        }
        if (bName == OutpostController.TypeName)
        {
            OutpostController outpost = selectedInstance.GetComponent<OutpostController>();
            return outpost != null ? outpost.DrawPanel(infoX, infoY, 340f) : empty;
        }
        if (bName == "Жилой дом" || bName == BuildingInstance.GuestHouseName)
        {
            bool guest = bName == BuildingInstance.GuestHouseName;
            var pm = PopulationManager.Instance;
            int cap = pm != null ? (guest ? pm.guestPerLevel : pm.HousePerLevel) * lvl : 0;
            int living = pm != null ? pm.ResidentsOf(selectedInstance) : 0;
            int athome = pm != null ? pm.AtHome(selectedInstance) : 0;

            float upY = PanelTop + 20f + 6f + UiKit.RowH + 4f;
            float h = upY + UiKit.SectionH + UpgradeRules.SectionHeight(selectedInstance) + 8f;
            PanelWindow(infoX, infoY, infoWidth, h, bName, lvl);

            float cy = infoY + PanelTop;
            Rect bar = new Rect(cx, cy, cw, 20f);
            float frac = cap > 0 ? living / (float)cap : 0f;
            UiKit.Bar(bar, frac, living >= cap && cap > 0 ? UiKit.Accent : UiKit.Good, (guest ? "Наёмников" : "Живёт") + $": {living}/{cap}");
            UiKit.Tip(bar, guest
                ? $"Сюда селятся только купленные на рынке бойцы — свои жители это место не займут.\n{(pm != null ? pm.guestPerLevel : 0)} мест на уровень."
                : $"Мест для горожан: {(pm != null ? pm.HousePerLevel : 0)} на уровень дома.");
            cy += 26f;
            UiKit.Row(new Rect(cx, cy, cw, UiKit.RowH), guest ? "Сейчас внутри" : "Сейчас дома", athome.ToString());

            float uy = UiKit.Section(cx, infoY + upY, cw, "Улучшение");
            UpgradeRules.DrawUpgradeSection(cx, uy, cw, selectedInstance);
            return new Rect(infoX, infoY, infoWidth + 5, h);
        }
        if (bName == "Мастерская")
        {
            WorkshopController workshop = selectedInstance.GetComponent<WorkshopController>();
            if (workshop == null) return empty;

            int recipeCount = 0;
            if (workshop.availableRecipes != null)
                foreach (var rc in workshop.availableRecipes) if (workshop.Unlocked(rc)) recipeCount++;

            string status = selectedInstance.isWorking ? ShiftStatus(selectedInstance) : selectedInstance.StatusText;
            float statusH = StatusH(status, cw);
            float barY = PanelTop;
            float recipesY = barY + 24f + statusH;
            float workersY = recipesY + UiKit.SectionH + workshop.Queue.Height + recipeCount * 34f + 2f;
            float upgradeY = workersY + WorkersRowH;
            float h = upgradeY + UiKit.SectionH + UpgradeRules.SectionHeight(selectedInstance) + 8f;

            PanelWindow(infoX, infoY, infoWidth, h, "Мастерская", lvl);

            // Что делает сейчас
            Rect br = new Rect(cx, infoY + barY, cw, 20f);
            if (workshop.IsBusy) UiKit.Bar(br, workshop.CraftProgress, UiKit.Info, StripTags(workshop.StatusLine));
            else LocGUI.Label(br, workshop.StatusLine, UiKit.MutedStyle);
            DrawStatus(cx, infoY + barY + 24f, cw, status);

            float btnY = UiKit.Section(cx, infoY + recipesY, cw, "Заказать");
            UiKit.Tip(new Rect(cx, infoY + recipesY, cw, UiKit.SectionH), TownHallStorage.QualityHelp);
            btnY = workshop.Queue.Draw(cx, btnY, cw, r => r.recipeName);
            if (workshop.availableRecipes != null)
            {
                foreach (var recipe in workshop.availableRecipes)
                {
                    if (!workshop.Unlocked(recipe)) continue;
                    // Лимит склада: готовое должно куда-то лечь (Цитадель или Хранилища)
                    int room = recipe.outputsTar ? StorageLimits.Free(StoreRes.Tar) : StorageLimits.RefinedRoom(recipe.kind);
                    bool fits = room >= recipe.outputAmount;
                    bool busy = workshop.IsBusy || workshop.Queue.Count > 0;
                    bool ok = busy ? !workshop.Queue.Full : workshop.CanAfford(recipe) && fits;
                    string label = (busy ? "+ " : "") + $"{recipe.recipeName}  {UiKit.Col(UiKit.MutedHex, recipe.CostText + " → " + recipe.outputAmount + " шт.")}";
                    string tip = busy ? (workshop.Queue.Full ? $"Очередь полна ({CraftQueue<CraftRecipe>.Max})" : "Мастерская занята — нажмите, чтобы поставить в очередь. Сырьё возьмём, когда начнётся работа")
                        : (!fits ? "Склад полон: готовое некуда положить. Улучшите Цитадель или постройте Хранилище"
                        : (ok ? $"Сырьё: {recipe.CostText}. Готово: {recipe.outputAmount} шт." : $"Не хватает сырья: нужно {recipe.CostText}"));
                    tip += $"\nВремя работы: {Balance.Dur(recipe.craftTime / workshop.LevelSpeed)}" + (workshop.LevelSpeed > 1f ? $" (мастерская Ур.{selectedInstance.level} — быстрее в {workshop.LevelSpeed:0.#} раза)" : "");
                    // Картинка того, что получится, слева от кнопки
                    string icon = ItemIcons.ByName(recipe.recipeName);
                    float lead = ItemIcons.Has(icon) ? 32f : 0f;
                    if (lead > 0f) ItemIcons.Draw(new Rect(cx, btnY + 2f, 26f, 26f), icon);
                    if (UiKit.Button(new Rect(cx + lead, btnY, cw - lead, 30f), label, ok, UiKit.Kind.Normal, tip))
                        workshop.Order(recipe);
                    btnY += 34f;
                }
            }

            if (PopulationManager.Instance != null)
                PopulationManager.Instance.DrawWorkersRow(cx, infoY + workersY, selectedInstance);

            float uy = UiKit.Section(cx, infoY + upgradeY, cw, "Улучшение");
            UpgradeRules.DrawUpgradeSection(cx, uy, cw, selectedInstance);
            return new Rect(infoX, infoY, infoWidth + 5, h);
        }
        if (bName == BarracksController.TypeName)
        {
            BarracksController barracks = selectedInstance.GetComponent<BarracksController>();
            if (barracks == null) return empty;
            if (barracks.PickingTarget) return barracks.DrawPickBanner(infoX, infoY, 420f);
            return barracks.DrawPanel(infoX, infoY, 460f);
        }
        if (bName == BuildingInstance.GateName)
        { var c = selectedInstance.GetComponent<GateController>(); return c != null ? c.DrawPanel(infoX, infoY, 330f) : empty; }
        if (bName == HealerHouse.TypeName)
        { var c = selectedInstance.GetComponent<HealerHouse>(); return c != null ? c.DrawPanel(infoX, infoY, 340f) : empty; }
        if (bName == MagicCannonController.TypeName)
        { var c = selectedInstance.GetComponent<MagicCannonController>(); return c != null ? c.DrawPanel(infoX, infoY, 340f) : empty; }
        if (bName == GroundCannonController.TypeName)
        { var c = selectedInstance.GetComponent<GroundCannonController>(); return c != null ? c.DrawPanel(infoX, infoY, 380f) : empty; }
        if (bName == MagicArmory.TypeName)
        { var c = selectedInstance.GetComponent<MagicArmory>(); return c != null ? c.DrawPanel(infoX, infoY, 420f) : empty; }
        if (bName == LifeAltar.TypeName)
        { var c = selectedInstance.GetComponent<LifeAltar>(); return c != null ? c.DrawPanel(infoX, infoY, 380f) : empty; }
        if (bName == EngineerBarracks.TypeName)
        { var c = selectedInstance.GetComponent<EngineerBarracks>(); return c != null ? c.DrawPanel(infoX, infoY, 460f) : empty; }
        if (bName == IndustrialPlant.TypeName)
        { var c = selectedInstance.GetComponent<IndustrialPlant>(); return c != null ? c.DrawPanel(infoX, infoY, 420f) : empty; }
        if (bName == WarBuilderYard.TypeName)
        { var c = selectedInstance.GetComponent<WarBuilderYard>(); return c != null ? c.DrawPanel(infoX, infoY, 400f) : empty; }
        if (bName == CourierHouse.TypeName)
        { var c = selectedInstance.GetComponent<CourierHouse>(); return c != null ? c.DrawPanel(infoX, infoY, 370f) : empty; }
        if (bName == WarehouseController.TypeName)
        { var c = selectedInstance.GetComponent<WarehouseController>(); return c != null ? c.DrawPanel(infoX, infoY, 360f) : empty; }
        if (bName == ArmoryController.TypeName)
        { var c = selectedInstance.GetComponent<ArmoryController>(); return c != null ? c.DrawPanel(infoX, infoY, 400f) : empty; }
        if (bName == StableController.TypeName)
        { var c = selectedInstance.GetComponent<StableController>(); return c != null ? c.DrawPanel(infoX, infoY, 380f) : empty; }
        if (bName == BerryStation.TypeName)
        { var c = selectedInstance.GetComponent<BerryStation>(); return c != null ? c.DrawPanel(infoX, infoY, 380f) : empty; }
        if (bName == KitchenController.TypeName)
        { var c = selectedInstance.GetComponent<KitchenController>(); return c != null ? c.DrawPanel(infoX, infoY, 400f) : empty; }
        if (bName == PotionHouse.TypeName)
        { var c = selectedInstance.GetComponent<PotionHouse>(); return c != null ? c.DrawPanel(infoX, infoY, 400f) : empty; }
        if (bName == WeaponForge.TypeName)
        { var c = selectedInstance.GetComponent<WeaponForge>(); return c != null ? c.DrawPanel(infoX, infoY, 440f) : empty; }
        if (bName == WagonYard.TypeName)
        { var c = selectedInstance.GetComponent<WagonYard>(); return c != null ? c.DrawPanel(infoX, infoY, 400f) : empty; }
        if (bName == FoundryController.TypeName)
        { var c = selectedInstance.GetComponent<FoundryController>(); return c != null ? c.DrawPanel(infoX, infoY, 430f) : empty; }
        if (bName == EngineerYard.TypeName)
        { var c = selectedInstance.GetComponent<EngineerYard>(); return c != null ? c.DrawPanel(infoX, infoY, 430f) : empty; }
        if (bName == MarketHouse.TypeName)
        { var c = selectedInstance.GetComponent<MarketHouse>(); return c != null ? c.DrawPanel(infoX, infoY, 400f) : empty; }
        if (bName == BuildingInstance.BridgeName)
        {
            var c = selectedInstance.GetComponent<BridgeController>();
            if (c == null) return empty;
            Rect pr = c.DrawPanel(infoX, infoY, 360f);
            return pr;
        }

        if (bName == "Башня")
        {
            TowerController tower = selectedInstance.GetComponent<TowerController>();
            float modH = EngineerYard.TowerSectionHeight(tower);
            float modY = PanelTop + UiKit.RowH * 3f + 4f;
            float upY = modY + modH;
            float h = upY + UiKit.SectionH + UpgradeRules.SectionHeight(selectedInstance) + 8f;
            PanelWindow(infoX, infoY, infoWidth, h, bName, lvl);

            float cy = infoY + PanelTop;
            string state = selectedInstance.IsRuined ? UiKit.Col(UiKit.BadHex, "Разрушена") : UiKit.Col(UiKit.GoodHex, "Готова к бою");
            LocGUI.Label(new Rect(cx, cy, cw, UiKit.RowH), state);
            cy += UiKit.RowH;
            if (tower != null)
            {
                UiKit.Row(new Rect(cx, cy, cw, UiKit.RowH), "Урон", $"{tower.ArrowDamage:F0}");
                UiKit.Row(new Rect(cx, cy + UiKit.RowH, cw, UiKit.RowH), "Радиус", $"{tower.Range:F1} кл.");
            }
            else LocGUI.Label(new Rect(cx, cy, cw, UiKit.RowH), "Обеспечивает оборону базы.", UiKit.MutedStyle);

            if (modH > 0f) EngineerYard.DrawTowerSection(cx, infoY + modY, cw, tower);
            float uy = UiKit.Section(cx, infoY + upY, cw, "Улучшение");
            UpgradeRules.DrawUpgradeSection(cx, uy, cw, selectedInstance);
            return new Rect(infoX, infoY, infoWidth + 5, h);
        }
        if (bName == BuildingInstance.WallName)
        {
            float upY = PanelTop + UiKit.RowH + 4f;
            float h = upY + UiKit.SectionH + UpgradeRules.SectionHeight(selectedInstance) + 8f;
            PanelWindow(infoX, infoY, infoWidth, h, bName, lvl);
            UiKit.Row(new Rect(cx, infoY + PanelTop, cw, UiKit.RowH), "Макс. прочность", $"{selectedInstance.MaxHp:F0}");
            float uy = UiKit.Section(cx, infoY + upY, cw, "Улучшение");
            UpgradeRules.DrawUpgradeSection(cx, uy, cw, selectedInstance);
            return new Rect(infoX, infoY, infoWidth + 5, h);
        }

        {
            // Обычное здание: добыча и склад
            var parts = new List<string>();
            System.Action<string> add = s => { if (s != null) parts.Add(s); };
            add(Stored("Золото", selectedInstance.storedGold, UiKit.GoldHex));
            add(Stored("Дерево", selectedInstance.storedWood, UiKit.WoodHex));
            add(Stored("Камень", selectedInstance.storedStone, UiKit.StoneHex));
            add(Stored("Еда", selectedInstance.storedFood, UiKit.FoodHex));
            add(Stored("Руда", selectedInstance.storedIron, "#B0C4DE"));
            add(Stored("Овёс", selectedInstance.storedOats, "#E8D890"));
            add(Stored("Ягоды", selectedInstance.storedBerries, "#F0A0C8"));
            add(Stored("Травы", selectedInstance.storedHerbs, "#90E060"));
            add(Stored("Смола", selectedInstance.storedResin, "#E0A040"));
            add(Stored("Листва", selectedInstance.storedLeaves, "#7FD060"));
            bool hasAny = parts.Count > 0;
            string resText = hasAny ? string.Join(UiKit.Col(UiKit.MutedHex, " · "), parts) : UiKit.Col(UiKit.MutedHex, "Склад пуст");

            string zoneTag = ResourceZones.BuildingZoneTag(selectedInstance);
            string status = selectedInstance.isWorking ? ShiftStatus(selectedInstance) : selectedInstance.StatusText;
            bool showHealth = selectedInstance.workers > 0;

            float statusH = StatusH(status, cw);
            float zoneH = string.IsNullOrEmpty(zoneTag) ? 0f : UiKit.RowH;
            float storeY = PanelTop + statusH + zoneH;
            float resH = UiKit.TextH(resText, cw, UiKit.Small) + 2f;
            float btnY = storeY + UiKit.SectionH + resH + 2f;
            float workersY = btnY + (hasAny ? 34f : 0f);
            float healthY = workersY + WorkersRowH;
            float upgradeY = healthY + (showHealth ? UiKit.RowH : 0f) + 4f;
            float h = upgradeY + UiKit.SectionH + UpgradeRules.SectionHeight(selectedInstance) + 8f;

            PanelWindow(infoX, infoY, infoWidth, h, bName, lvl);

            // Что не так - сразу под шапкой
            DrawStatus(cx, infoY + PanelTop, cw, status);
            if (zoneH > 0f)
            {
                Rect zr = new Rect(cx, infoY + PanelTop + statusH, cw, UiKit.RowH);
                LocGUI.Label(zr, zoneTag, UiKit.Small);
                UiKit.Tip(zr, "Зона ресурса под зданием: в богатой зоне добыча быстрее.");
            }

            float sy = UiKit.Section(cx, infoY + storeY, cw, $"Склад · лимит {selectedInstance.Capacity}");
            LocGUI.Label(new Rect(cx, sy, cw, resH), resText, UiKit.Small);

            if (hasAny)
            {
                float halfW = (cw - 6f) / 2f;
                string hereLabel = selectedInstance.courierOnTheWay ? "Курьер идёт..." : "Собрать";
                if (UiKit.Button(new Rect(cx, infoY + btnY, halfW, UiKit.BtnH), hereLabel, !selectedInstance.courierOnTheWay, UiKit.Kind.Primary,
                        selectedInstance.courierOnTheWay ? "Курьер уже идёт за добычей" : "Курьер отнесёт добычу отсюда в Цитадель"))
                    CollectFrom(selectedInstance);

                if (UiKit.Button(new Rect(cx + halfW + 6f, infoY + btnY, halfW, UiKit.BtnH), "Со всех", UiKit.Kind.Normal,
                        $"Собрать добычу со всех зданий «{bName}»"))
                    CollectAllOfType(selectedInstance.type);
            }

            if (PopulationManager.Instance != null)
                PopulationManager.Instance.DrawWorkersRow(cx, infoY + workersY, selectedInstance);

            if (showHealth)
            {
                int hpPct = Mathf.RoundToInt(selectedInstance.WorkersHealth * 100f);
                int effPct = Mathf.RoundToInt(selectedInstance.HealthFactor * 100f);
                string hcol = effPct >= 95 ? UiKit.GoodHex : (effPct >= 75 ? UiKit.WarnHex : UiKit.BadHex);
                Rect hr = new Rect(cx, infoY + healthY, cw, UiKit.RowH);
                UiKit.Row(hr, "Самочувствие рабочих", UiKit.Col(hcol, $"{hpPct}%  → {effPct}%"));
                UiKit.Tip(hr, $"Голодные и раненые рабочие работают медленнее.\nСамочувствие {hpPct}% → выработка {effPct}%.");
            }

            float uy = UiKit.Section(cx, infoY + upgradeY, cw, "Улучшение");
            UpgradeRules.DrawUpgradeSection(cx, uy, cw, selectedInstance);
            return new Rect(infoX, infoY, infoWidth + 5, h);
        }
    }

    // Убрать теги <b>, <color> и т.п. (для текста поверх полоски)
    static string StripTags(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        bool tag = false;
        foreach (char ch in s)
        {
            if (ch == '<') { tag = true; continue; }
            if (ch == '>') { tag = false; continue; }
            if (!tag) sb.Append(ch);
        }
        return sb.ToString();
    }

    // ---------- Окно построек ----------

    const float CardW = 240f, CardH = 118f, CardGap = 10f, IconS = 76f;
    const int ShopCols = 3;

    // Вкладки окна построек (группы выводятся из названий зданий; механика не меняется)
    static readonly string[] ShopTabs = { "Все", "Хозяйство", "Ремёсла", "Оборона", "Дороги" };
    private int shopTab = 0;

    static int CategoryOf(string n)
    {
        if (BuildingInstance.IsRoadName(n) || n == BuildingInstance.BridgeName) return 4;
        if (n == BuildingInstance.WallName || n == BuildingInstance.GateName || n == "Башня" || n == BarracksController.TypeName
            || n == MagicCannonController.TypeName || n == GroundCannonController.TypeName || n == EngineerBarracks.TypeName
            || n == WarBuilderYard.TypeName) return 3;
        if (n == "Мастерская" || n == ArmoryController.TypeName || n == StableController.TypeName || n == KitchenController.TypeName
            || n == PotionHouse.TypeName || n == WeaponForge.TypeName || n == FoundryController.TypeName || n == EngineerYard.TypeName
            || n == MagicArmory.TypeName || n == LifeAltar.TypeName || n == IndustrialPlant.TypeName) return 2;
        return 1;   // жильё, добыча, склады, торговля, аванпост
    }

    void DrawShop()
    {
        var shown = new List<int>();
        for (int k = 0; k < buildingTypes.Length; k++)
        {
            if (buildingTypes[k] == null || !PathAllows(buildingTypes[k].name)) continue;
            if (shopTab > 0 && CategoryOf(buildingTypes[k].name) != shopTab) continue;
            shown.Add(k);
        }
        int n = shown.Count;
        int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)ShopCols));
        float contentH = rows * (CardH + CardGap);

        const float tabsH = 28f, footH = 28f;
        float top = UiKit.Phone ? 42f : 56f, bottomGap = UiKit.Phone ? 4f : 50f;   // телефон: окно во всю высоту под полосой казны
        const float headH = UiKit.TitleH + 6f + tabsH + 8f;
        float maxViewH = Mathf.Max(CardH + CardGap, UiKit.H - top - bottomGap - headH - footH);
        float viewH = Mathf.Min(contentH, maxViewH);
        bool scrolling = contentH > viewH + 0.5f;
        shopMaxScroll = Mathf.Max(0f, contentH - viewH);
        shopScroll = Mathf.Clamp(shopScroll, 0f, shopMaxScroll);

        const float barW = 16f;
        float w = ShopCols * CardW + (ShopCols + 1) * CardGap + (scrolling ? barW : 0f);
        float h = headH + viewH + footH;
        float x = (UiKit.W - w) / 2f;
        float y = Mathf.Max(top, (UiKit.H - h) / 2f);
        shopRect = new Rect(x, y, w, h);

        UiKit.Window(shopRect, "Постройки", out bool close);
        if (close) { SetShopOpen(false); return; }

        int tab = UiKit.Tabs(new Rect(x + CardGap, y + UiKit.TitleH + 6f, w - CardGap * 2f, tabsH - 2f), ShopTabs, shopTab);
        if (tab != shopTab) { shopTab = tab; shopScroll = 0f; }

        Rect view = new Rect(x, y + headH, w - (scrolling ? barW : 0f), viewH);
        bool mouseInView = view.Contains(Event.current.mousePosition);

        GUI.BeginGroup(view);
        for (int i = 0; i < n; i++)
        {
            int col = i % ShopCols, row = i / ShopCols;
            Rect r = new Rect(CardGap + col * (CardW + CardGap), row * (CardH + CardGap) - shopScroll, CardW, CardH);
            if (r.yMax < 0f || r.y > viewH) continue;
            DrawCard(shown[i], r, mouseInView);
        }
        if (n == 0) LocGUI.Label(new Rect(CardGap, 10f, w - CardGap * 2f, 22f), "Здесь пока ничего нет", UiKit.MutedStyle);
        GUI.EndGroup();

        if (scrolling)
        {
            shopScroll = GUI.VerticalScrollbar(new Rect(view.xMax, view.y, barW - 2f, viewH), shopScroll, viewH, 0f, contentH);
        }

        GUIStyle foot = new GUIStyle(UiKit.MutedStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        Rect fr = new Rect(x, shopRect.yMax - footH, w, footH - 4f);
        LocGUI.Label(fr, TouchInput.Active ? "Только в зелёной зоне  ·  список листается пальцем  ·  «Назад» — закрыть"
                                           : "Только в зелёной зоне  ·  Shift — несколько  ·  ПКМ / Esc — отмена", foot);
        UiKit.Tip(fr, "Строить можно только в зелёной зоне (вокруг Цитадели и аванпостов).\nЛКМ — поставить, Shift — поставить несколько подряд, ПКМ / Esc — убрать из рук." +
            (scrolling ? "\nКолёсико — прокрутка списка." : "") + "\nЦифры 1–9 — быстро взять постройку в руки.");
    }

    void DrawCard(int i, Rect r, bool clickable = true)
    {
        BuildingType bt = buildingTypes[i];
        int count = CountOfType(bt.name);
        bool resOk = HasResources(bt);
        string extra = UpgradeRules.ShortBuildNeeds(bt.name, count, out bool extraOk);
        bool limit = AtLimit(bt);
        bool hallOk = HallLevelOk(bt);
        string need = hallOk ? NeedProblem(bt) : "";
        bool can = resOk && extraOk && !limit && hallOk && need == "";

        bool clicked = LocGUI.Button(r, GUIContent.none, UiKit.ButtonStyle(can ? UiKit.Kind.Normal : UiKit.Kind.Ghost));
        if (clicked && clickable)
        {
            if (can) { TakeInHand(i); return; }
            if (limit) { WarManager.Notify($"{bt.name}: {LimitText(bt).ToLower()}", false); return; }
            if (!hallOk) { WarManager.Notify($"{bt.name}: нужна Цитадель Ур.{HallLevelNeeded(bt.name)}", false); return; }
            if (need != "") { WarManager.Notify($"{bt.name}: {need.Substring(0, 1).ToLower()}{need.Substring(1)}", false); return; }

            string miss = MissingLine(bt);
            if (!extraOk) miss += (miss != "" ? " и " : "") + "материалов из мастерской";
            WarManager.Notify($"{bt.name}: не хватает {miss}", false);
            return;
        }

        // Картинка здания (как оно выглядит в городе)
        Rect icon = new Rect(r.x + 8f, r.y + 8f, IconS, IconS);
        DrawBuildingIcon(icon, bt, can);
        float tx = icon.xMax + 8f, tw = r.xMax - tx - 8f;

        GUIStyle head = new GUIStyle(UiKit.Body) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
        head.normal.textColor = can ? UiKit.Text : UiKit.Muted;
        GUIStyle line = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        GUIStyle desc = new GUIStyle(UiKit.MutedStyle) { wordWrap = false, clipping = TextClipping.Clip };
        GUIStyle key = new GUIStyle(UiKit.MutedStyle) { alignment = TextAnchor.UpperRight, wordWrap = false };

        LocGUI.Label(new Rect(tx, r.y + 6f, tw - (i < 9 ? 26f : 0f), 22f), bt.name, head);
        if (i < 9) LocGUI.Label(new Rect(r.xMax - 34f, r.y + 7f, 26f, 20f), $"[{i + 1}]", key);

        LocGUI.Label(new Rect(tx, r.y + 28f, tw, 18f), Describe(bt), desc);
        LocGUI.Label(new Rect(tx, r.y + 47f, tw, 18f), CostLine(bt), line);
        if (extra != "") LocGUI.Label(new Rect(tx, r.y + 65f, tw, 18f), extra, line);

        // Статус-чип
        string chip; Color chipCol;
        string cnt = CountChip(bt);
        bool defFull = IsDefenseType(bt.name) && DefenseCount() >= DefenseMax();
        if (can) { chip = "Можно строить" + (cnt != "" ? " · " + cnt : ""); chipCol = UiKit.Good; }
        else if (limit && defFull) { chip = $"Оборона {DefenseCount()}/{DefenseMax()}"; chipCol = UiKit.Warn; }
        else if (limit) { chip = MaxOfType(bt.name) == 1 ? "Уже построен" : $"Не больше {MaxOfType(bt.name)}"; chipCol = UiKit.Warn; }
        else if (!hallOk) { chip = $"Цитадель Ур.{HallLevelNeeded(bt.name)}"; chipCol = UiKit.Warn; }
        else if (need != "") { chip = $"Нужна Ферма Ур.{BuildingInstance.OatsFromLevel}"; chipCol = UiKit.Warn; }
        else { chip = "Не хватает"; chipCol = UiKit.Bad; }
        DrawChip(new Rect(tx, r.yMax - 26f, 0f, 18f), chip, chipCol);

        if (count > 0)
        {
            // Сколько уже стоит в городе - бейдж в углу картинки
            string cs = "×" + count;
            GUIStyle badge = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
            badge.normal.textColor = Color.white;
            float bw = badge.CalcSize(Loc.Content(cs)).x + 10f;
            Rect br = new Rect(icon.xMax - bw - 2f, icon.yMax - 18f, bw, 16f);
            Color oc = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(br, Texture2D.whiteTexture);
            GUI.color = oc;
            LocGUI.Label(br, cs, badge);
            UiKit.Tip(br, $"Построено: {count}");
        }

        if (clickable) UiKit.Tip(r, CardTip(bt, count, can, limit, hallOk, extraOk));
    }

    static Texture2D iconFrame;

    /// <summary>Картинка здания в рамке (у недоступных - бледнее). Без картинки - цветная плашка с буквой.</summary>
    public static void DrawBuildingIcon(Rect r, BuildingType bt, bool active)
    {
        if (iconFrame == null)
        {
            // Тёмная подложка со светлой каймой (сверху чуть светлее)
            const int S = 32;
            iconFrame = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "IconFrame" };
            var px = new Color32[S * S];
            for (int yy = 0; yy < S; yy++)
                for (int xx = 0; xx < S; xx++)
                {
                    float edge = Mathf.Min(Mathf.Min(xx, S - 1 - xx), Mathf.Min(yy, S - 1 - yy));
                    float v = Mathf.Lerp(0.11f, 0.20f, yy / (float)(S - 1));   // y=0 - низ текстуры
                    Color c = edge < 1f ? new Color(0.45f, 0.42f, 0.36f, 1f) : new Color(v * 0.9f, v * 0.95f, v * 0.85f, 0.95f);
                    px[yy * S + xx] = c;
                }
            iconFrame.SetPixels32(px);
            iconFrame.Apply();
        }
        GUI.DrawTexture(r, iconFrame, ScaleMode.StretchToFill);

        Color old = GUI.color;
        Sprite sp = bt != null ? GameArt.Building(bt.name, 1) : null;
        Rect inner = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f);
        if (sp != null && sp.texture != null)
        {
            GUI.color = active ? Color.white : new Color(0.75f, 0.75f, 0.75f, 0.55f);
            Texture tex = sp.texture;
            Rect tr = sp.textureRect;
            Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            // Сохраняем пропорции картинки
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            Rect fit;
            if (aspect > 1f) { float h = inner.width / aspect; fit = new Rect(inner.x, inner.y + (inner.height - h) * 0.5f, inner.width, h); }
            else { float w = inner.height * aspect; fit = new Rect(inner.x + (inner.width - w) * 0.5f, inner.y, w, inner.height); }
            GUI.DrawTextureWithTexCoords(fit, tex, uv);
        }
        else if (bt != null)
        {
            GUI.color = active ? bt.color : bt.color * 0.6f;
            GUI.DrawTexture(new Rect(inner.x + 8f, inner.y + 8f, inner.width - 16f, inner.height - 16f), Texture2D.whiteTexture);
            GUI.color = old;
            var st = new GUIStyle(UiKit.Title) { alignment = TextAnchor.MiddleCenter, fontSize = 28 };
            st.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            string n = Loc.T(bt.name);
            GUI.Label(inner, string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpperInvariant(), st);
        }
        GUI.color = old;
    }

    // Подробности карточки - в подсказке
    string CardTip(BuildingType bt, int count, bool can, bool limit, bool hallOk, bool extraOk)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b>{bt.name}</b>  {UiKit.Col(UiKit.MutedHex, bt.size.x + "×" + bt.size.y + " кл.")}\n");
        sb.Append(Describe(bt));
        sb.Append("\nЦена: " + CostLine(bt));
        int step = Balance.CopyStepPercent(bt);
        if (step > 0) sb.Append("\n" + UiKit.Col(UiKit.MutedHex, count > 0
            ? $"Каждое следующее такое здание дороже на {step}% — выгоднее улучшать то, что есть"
            : $"Каждое следующее такое здание будет дороже на {step}%"));
        string mats = UpgradeRules.DescribeBuild(bt.name, count);
        if (mats != "") sb.Append("\n" + mats);
        if (!hallOk) sb.Append("\n" + UiKit.Col(UiKit.WarnHex, $"Нужна Цитадель Ур.{HallLevelNeeded(bt.name)}"));
        else if (NeedProblem(bt) != "") sb.Append("\n" + UiKit.Col(UiKit.WarnHex, NeedProblem(bt)));
        if (limit) sb.Append("\n" + UiKit.Col(UiKit.WarnHex, LimitText(bt)));
        string miss = MissingLine(bt);
        if (miss != "") sb.Append("\n" + UiKit.Col(UiKit.BadHex, "Не хватает: " + miss));
        if (can) sb.Append("\n" + UiKit.Col(UiKit.MutedHex, IsPaintType(bt) ? "Клик — взять в руки, ЛКМ можно зажать" : "Клик — взять в руки"));
        return sb.ToString();
    }

    static void DrawChip(Rect r, string text, Color col)
    {
        GUIStyle st = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontStyle = FontStyle.Bold };
        st.normal.textColor = col;
        float w = st.CalcSize(Loc.Content(text)).x + 14f;
        Rect cr = new Rect(r.x, r.y, w, r.height);
        Color old = GUI.color;
        GUI.color = new Color(col.r, col.g, col.b, 0.16f);
        GUI.DrawTexture(cr, Texture2D.whiteTexture);
        GUI.color = old;
        LocGUI.Label(cr, text, st);
    }

    string DoorHint()
    {
        if (placeDoorProblem == 1) return "\n" + UiKit.Col(UiKit.WarnHex, "Нужна свободная сторона для двери");
        if (placeDoorProblem == 2) return "\n" + UiKit.Col(UiKit.WarnHex, "Закроет выход соседу");
        return "";
    }

    static string ZoneHint(BuildingType t)
    {
        if (BuildingInstance.IsRoadName(t.name)) return "Дорогу ведут от зелёной зоны или от подключённой дороги";
        if (t.name == OutpostController.TypeName) return "Аванпост — у границы зелёной зоны или у дороги из неё";
        return "Вне зелёной зоны строить нельзя";
    }

    void DrawCursorTip()
    {
        BuildingType t = Current;
        if (t == null) return;

        string reason;
        if (BuildBlocked) reason = UiKit.Col(UiKit.BadHex, "Идёт война — строить нельзя");
        else if (!HallLevelOk(t)) reason = UiKit.Col(UiKit.BadHex, $"Нужна Цитадель Ур.{HallLevelNeeded(t.name)}");
        else if (NeedProblem(t) != "") reason = UiKit.Col(UiKit.BadHex, NeedProblem(t));
        else if (placeWaterProblem != "") reason = UiKit.Col(UiKit.WarnHex, placeWaterProblem);
        else if (!placeZoneOk) reason = UiKit.Col(UiKit.WarnHex, ZoneHint(t));
        else if (!placeSpaceOk) reason = UiKit.Col(UiKit.BadHex, "Здесь занято");
        else if (placeDoorProblem == 1) reason = UiKit.Col(UiKit.BadHex, "Нужна свободная сторона для двери");
        else if (placeDoorProblem == 2) reason = UiKit.Col(UiKit.BadHex, "Закроет выход соседнему зданию");
        else if (AtLimit(t)) reason = UiKit.Col(UiKit.BadHex, LimitText(t));
        else if (!HasResources(t)) reason = UiKit.Col(UiKit.BadHex, "Не хватает: " + MissingLine(t));
        else if (!UpgradeRules.CanPayBuild(t.name, CountOfType(t.name)))
            reason = UpgradeRules.ShortBuildNeeds(t.name, CountOfType(t.name), out _);
        else if (UiKit.Phone) reason = UiKit.Col(UiKit.GoodHex, IsPaintType(t) ? "«Линией» — вести пальцем, ставится по пути" : "«Подряд» — поставить несколько таких");
        else reason = UiKit.Col(UiKit.GoodHex, IsPaintType(t) ? "ЛКМ — ставить (можно зажать)" : "ЛКМ — поставить   Shift — ещё");

        if (t.name == KitchenController.TypeName && placeZoneOk && placeSpaceOk)
            reason += "\n" + UiKit.Col(UiKit.InfoHex, "Вплотную к казарме — похлёбка без курьеров");

        string text = $"<b>{t.name}</b>\n{reason}\n{UiKit.Col(UiKit.MutedHex, TouchInput.Active ? "«✓ Поставить» внизу · «✕ Отмена» — убрать из рук" : "ПКМ / Esc — убрать из рук")}";

        Vector2 m = Mouse.current.position.ReadValue();
        float w = 290f;
        float h = UiKit.TextH(text, w - 16f) + 10f;
        float x = Mathf.Min(UiKit.ToGui(m).x + 20f, UiKit.W - w - 4f);
        float y = Mathf.Min(UiKit.ToGui(m).y + 20f, UiKit.H - h - 4f);
        Rect r = new Rect(x, y, w, h);

        LocGUI.Box(r, GUIContent.none);
        LocGUI.Label(new Rect(r.x + 8f, r.y + 5f, r.width - 16f, r.height - 8f), text);
    }

    Rect durabilityRect;   // плашка прочности под панелью здания (клики по ней не уходят на карту)

    void DrawDurability(BuildingInstance b)
    {
        if (b == null || !b.IsDamageable || b.UnderConstruction) { durabilityRect = new Rect(0f, 0f, 0f, 0f); return; }

        // Военный строитель: переключатель «Чинить в войну» прямо под прочностью
        bool markRow = WarBuilderYard.Any && WarBuilderYard.CanMark(b);
        Rect p = infoPanelRect;
        Rect r = new Rect(p.x, p.yMax + 4f, p.width - 5f, markRow ? 74f : 46f);
        durabilityRect = r;
        LocGUI.Box(r, GUIContent.none);

        if (markRow)
        {
            bool on = WarBuilderYard.IsMarked(b);
            Rect tr = new Rect(r.x + 10f, r.y + 46f, r.width - 20f, 22f);
            string cap = $" Чинить в войну  ({WarBuilderYard.MarkedCount}/{WarBuilderYard.MaxMarks})";
            bool want = LocGUI.Toggle(tr, on, cap);
            if (want != on) WarBuilderYard.SetMarked(b, want);
            UiKit.Tip(tr, "Строители Двора военного строителя в войну бегут чинить отмеченные здания в первую очередь — прямо под ударами.\n" +
                          "Над отмеченным зданием висит оранжевый молоток.");
        }

        string text;
        float frac;
        Color col;
        if (b.IsRuined)
        {
            frac = b.RebuildPaid ? b.RebuildProgress : 0f;
            col = UiKit.Warn;
            text = UiKit.Col(UiKit.WarnHex, UiKit.B("Руины")) + " — " + b.RebuildStatus;
        }
        else
        {
            frac = b.Hp / b.MaxHp;
            col = Color.Lerp(UiKit.Bad, UiKit.Good, frac);
            string state = b.UnderAttack
                ? "  " + UiKit.Col(UiKit.BadHex, "под ударом")
                : (frac < 0.999f ? "  " + UiKit.Col(UiKit.MutedHex, "чинится") : "");
            text = $"Прочность{state}";
        }

        GUIStyle st = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 20f), text, st);
        string barText = b.IsRuined ? (b.RebuildPaid ? $"{frac * 100f:F0}%" : null) : $"{Mathf.CeilToInt(b.Hp)}/{Mathf.CeilToInt(b.MaxHp)}";
        UiKit.Bar(new Rect(r.x + 10f, r.y + 26f, r.width - 20f, 14f), frac, col, barText);
    }

    // ---------- Кнопки «Перенести» и «Снести» под панелью здания ----------
    // На телефоне без них перенос и снос прятались в долгом нажатии на здание. Теперь они всегда на виду
    // (на компьютере тоже - с подсказкой про клавиши M и X).

    const float ActionsH = 36f;
    Rect actionsRect;   // клики по кнопкам не уходят на карту

    static float DurabilityH(BuildingInstance b)
    {
        if (b == null || !b.IsDamageable || b.UnderConstruction) return 0f;
        return (WarBuilderYard.Any && WarBuilderYard.CanMark(b)) ? 74f : 46f;
    }

    bool ShowActions(BuildingInstance b) { return b != null && b.gameObject != null && !isMoving && !isBuildingMode && !BuildBlocked; }

    // Сколько места занимает то, что под панелью (прочность и кнопки)
    float UnderPanelH()
    {
        BuildingInstance b = selectedInstance;
        float h = 0f, d = DurabilityH(b);
        if (d > 0f) h += d + 4f;
        if (ShowActions(b)) h += ActionsH + 4f;
        return h;
    }

    void DrawBuildingActions(BuildingInstance b)
    {
        if (!ShowActions(b)) { actionsRect = new Rect(0f, 0f, 0f, 0f); return; }
        bool canMove = !b.IsTownHall && !b.IsBridge;
        bool canDemolish = b.gameObject.name != (townHall != null ? townHall.name : "") && !starterBuildings.Contains(b.gameObject);
        if (!canMove && !canDemolish) { actionsRect = new Rect(0f, 0f, 0f, 0f); return; }

        Rect p = infoPanelRect;
        float y = durabilityRect.height > 0f ? durabilityRect.yMax + 4f : p.yMax + 4f;
        Rect r = new Rect(p.x, y, p.width - 5f, ActionsH);
        actionsRect = r;
        LocGUI.Box(r, GUIContent.none);

        bool phone = UiKit.Phone;
        float bw = canMove && canDemolish ? (r.width - 12f - 6f) * 0.5f : r.width - 12f;
        Rect br = new Rect(r.x + 6f, r.y + 4f, bw, r.height - 8f);
        if (canMove)
        {
            if (UiKit.Button(br, "✥ Перенести", UiKit.Kind.Normal,
                    phone ? "Взять здание в руки и поставить в другом месте: ведите пальцем, потом «✓ Поставить». Бесплатно."
                          : "Перенести здание в другое место (M). Бесплатно."))
                StartMoveSelected(b);
            br.x += bw + 6f;
        }
        if (canDemolish)
        {
            string tip = b.UnderConstruction ? "Отменить стройку: вернётся вся цена (X)" : "Снести здание: вернётся часть цены, спросим ещё раз (X)";
            if (UiKit.Button(br, b.UnderConstruction ? "✖ Отменить" : "✖ Снести", UiKit.Kind.Danger, phone ? tip.Replace(" (X)", "") : tip))
                AskDemolish(b.gameObject);
        }
    }

    // Перенос из панели: на телефоне призрак появляется там, где стояло здание, а не в центре экрана
    void StartMoveSelected(BuildingInstance b)
    {
        GameObject go = b.gameObject;
        StartMove(go);
        if (!isMoving || cam == null) return;
        if (TouchInput.Active && TouchInput.Instance != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(b.Center);
            TouchInput.Instance.PlaceCursor(new Vector2(sp.x, sp.y));
        }
    }
}
