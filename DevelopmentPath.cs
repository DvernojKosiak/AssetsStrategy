using UnityEngine;

// Unknown - старое значение "скрытого" пути, оставлено для совместимости (нигде не выбирается)
public enum PathKind { None, Magic, War, Unknown, Engineering }

// Школа магии (путь Магии, Цитадель Ур.3): выбирается один раз, внутри пути
public enum MagicSchool { None, Nature, Life, Chaos }

// Направление инженеров (путь Инженерии, Цитадель Ур.3): выбирается один раз
public enum EngSchool { None, Military, Pyro, Industrial }
public enum WarSchool { None, Heavy, Builder, Camo }

/// <summary>
/// Путь развития. Когда Цитадель дорастает до RequiredHallLevel, игрок покупает "ячейку направления"
/// и выбирает один путь - навсегда:
///  - Магия: собиратели (Станция Ур.2+) ходят в лес за травами, Зельеварня варит зелье отравления,
///    Оружейная отравляет луки, в казарме (Ур.2) - Маг-отравитель (бьёт ядом по площади).
///    Всё магическое зависит от маны в воздухе (ManaManager.Factor).
///  - Военное дело: Кузня мастера - доводка готового оружия мини-игрой "точный удар",
///    переделка обычных ловушек в боевые (ставятся в бою и в набеге, см. Traps.cs).
///  - Пушки (Цитадель Ур.3, всем): Магия - снаряды, Военное дело - полевая пушка, Инженерия - подъёмник и бронебойное ядро.
///  - Инженерия: Литейная отливает детали из руды и стали (котлы можно раскочегарить - быстрее,
///    но можно перегреть: авария или пожар), Инженерный двор собирает из деталей модули для построек
///    (первый - Паровой отбойник для башни: отбрасывает врагов) и ловушки из резины (см. Traps.cs).
/// Школа магии (Цитадель Ур.3, только путь Магии, тоже навсегда):
///  - Природа (NatureMagic.cs): лечит своих у рек, чуть ускоряет постройки у рек и лесов, временами видно всё в лесах.
///  - Жизнь (LifeAltar.cs): Алтарь жизни растит голема из ресурсов; голем сторожит алтарь, ходит в набег; алтарь сломан - голем гибнет.
///  - Хаос (ChaosMagic.cs): Магическая оружейная делает ядро хаоса для Мага стихий - иногда бросает чёрную дыру.
/// Направление инженеров (Цитадель Ур.3, только путь Инженерии, навсегда):
///  - Военный инженер (Catapult.cs): Инженерная казарма собирает катапульты (1 человек + материалы).
///  - Пиротехник (Pyrotechnics.cs): модуль башни «Фейерверочный лук» и ловушка «Салют».
///  - Индустриализатор (IndustrialPlant.cs): Завод - площадка, производства рядом работают быстрее на топливе; перегрев - взрыв.
/// Здания и войска пути видны в постройках / казарме только тому, кто его выбрал.
/// Отдельная кнопка "Путь" слева от "Город". Создаётся в BuildingPlacer.Start.
/// </summary>
public class DevelopmentPath : MonoBehaviour
{
    public static DevelopmentPath Instance { get; private set; }

    public static PathKind Chosen { get; private set; } = PathKind.None;
    public static bool IsMagic => Chosen == PathKind.Magic;
    public static bool IsWar => Chosen == PathKind.War;
    public static bool IsEngineering => Chosen == PathKind.Engineering;

    public static MagicSchool School { get; private set; } = MagicSchool.None;
    public static bool IsNature => IsMagic && School == MagicSchool.Nature;
    public static bool IsLife => IsMagic && School == MagicSchool.Life;
    public static bool IsChaos => IsMagic && School == MagicSchool.Chaos;

    /// <summary>Доступно школе: школа не нужна или выбрана именно она (и путь - Магия).</summary>
    public static bool SchoolAllows(MagicSchool need) { return need == MagicSchool.None || (IsMagic && School == need); }

    public static EngSchool Eng { get; private set; } = EngSchool.None;
    public static bool IsMilitaryEng => IsEngineering && Eng == EngSchool.Military;
    public static bool IsPyro => IsEngineering && Eng == EngSchool.Pyro;
    public static bool IsIndustrial => IsEngineering && Eng == EngSchool.Industrial;

    /// <summary>Доступно направлению инженеров: не нужно или выбрано именно оно (и путь - Инженерия).</summary>
    public static bool EngAllows(EngSchool need) { return need == EngSchool.None || (IsEngineering && Eng == need); }

    public static WarSchool War { get; private set; } = WarSchool.None;
    public static bool IsHeavy => IsWar && War == WarSchool.Heavy;
    public static bool IsWarBuilder => IsWar && War == WarSchool.Builder;
    public static bool IsCamo => IsWar && War == WarSchool.Camo;

    /// <summary>Доступно ветке военного дела: не нужна или выбрана именно она (и путь - Военное дело).</summary>
    public static bool WarAllows(WarSchool need) { return need == WarSchool.None || (IsWar && War == need); }

    // Unity с отключённым Domain Reload не сбрасывает static между запусками Play.
    // Выбор пути должен жить только в рамках сессии (потом его заменит сохранение игры).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Chosen = PathKind.None;
        School = MagicSchool.None;
        Eng = EngSchool.None;
        War = WarSchool.None;
        windowShown = false;
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ResetBeforeScene()
    {
        Chosen = PathKind.None;
        School = MagicSchool.None;
        Eng = EngSchool.None;
        War = WarSchool.None;
        windowShown = false;
    }

    // ---------- Цена ячейки направления ----------
    public const int RequiredHallLevel = 2;
    public const int CostGold = 500, CostWood = 150, CostStone = 150;

    // ---------- Цена школы магии ----------
    public const int SchoolHallLevel = 3;
    public const int SchoolGold = 400, SchoolHerbs = 10;
    public const int EngGold = 400, EngParts = 15;
    public const int WarGold = 400, WarIron = 30;

    bool open;
    bool adminOpen;                     // строка админа: сменить путь бесплатно, в любой момент
    PathKind confirm = PathKind.None;   // ждём подтверждения выбора
    MagicSchool confirmSchool = MagicSchool.None;
    EngSchool confirmEng = EngSchool.None;
    WarSchool confirmWar = WarSchool.None;
    int tab;                            // 0 - пути, 1 - школа магии

    // Левее «Город». Ширина - по надписи («✦ Военное дело (!)» и перевод не влезали в 104 точки и обрезались);
    // кнопки «Клан» и «Домой» встают левее неё (ButtonLeft)
    static float buttonW = 104f;
    static Rect ButtonRect => new Rect(ButtonLeft, UiKit.TopBtnY, buttonW, UiKit.TopBtnH);

    /// <summary>Левый край кнопки «Путь».</summary>
    public static float ButtonLeft => UiKit.W - (UiKit.Phone ? 160f : 156f) - buttonW;
    static Rect windowRect;
    static bool windowShown;

    /// <summary>Здание / войско доступно: путь не нужен или выбран именно он.</summary>
    public static bool Allows(PathKind need) { return need == PathKind.None || need == Chosen; }

    public static string SchoolName(MagicSchool k)
    {
        switch (k)
        {
            case MagicSchool.Nature: return "Магия природы";
            case MagicSchool.Life: return "Магия жизни";
            case MagicSchool.Chaos: return "Магия хаоса";
            default: return "не выбрана";
        }
    }

    public static string EngName(EngSchool k)
    {
        switch (k)
        {
            case EngSchool.Military: return "Военный инженер";
            case EngSchool.Pyro: return "Пиротехник";
            case EngSchool.Industrial: return "Индустриализатор";
            default: return "не выбрано";
        }
    }

    public static string WarName(WarSchool k)
    {
        switch (k)
        {
            case WarSchool.Heavy: return "Тяжёлая стратегия";
            case WarSchool.Builder: return "Военный строитель";
            case WarSchool.Camo: return "Мастер маскировки";
            default: return "не выбрана";
        }
    }

    public static string PathName(PathKind k)
    {
        switch (k)
        {
            case PathKind.Magic: return "Магия";
            case PathKind.War: return "Военное дело";
            case PathKind.Engineering: return "Инженерия";
            case PathKind.Unknown: return "???";
            default: return "не выбран";
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Chosen = PathKind.None;   // TODO: когда появится сохранение игры - читать выбор отсюда
        School = MagicSchool.None;
        Eng = EngSchool.None;
        War = WarSchool.None;
    }

    // Тревога (WarManager.RaiseAlarm): свернуть окно, чтобы было видно бой
    void OnEnable() { WarManager.OnAlarm += CollapseForAlarm; }
    void OnDisable() { WarManager.OnAlarm -= CollapseForAlarm; }
    bool CollapseForAlarm()
    {
        if (!open) return false;
        open = false;
        confirm = PathKind.None;
        confirmSchool = MagicSchool.None;
        return true;
    }

    void OnDestroy()
    {
        if (Instance == this) { Instance = null; Chosen = PathKind.None; School = MagicSchool.None; Eng = EngSchool.None; War = WarSchool.None; windowShown = false; }
    }

    /// <summary>Открытое окно пути закрывает этот прямоугольник (кнопки под ним тогда не рисуют).</summary>
    public static bool WindowCovers(Rect r) { return Instance != null && windowShown && windowRect.Overlaps(r); }

    public static bool IsPointerOver(Vector2 gp)
    {
        if (Instance == null) return false;
        return ButtonRect.Contains(gp) || (windowShown && windowRect.Contains(gp));
    }

    static BuildingInstance Hall()
    {
        var pm = PopulationManager.Instance;
        return pm != null ? pm.Hall : null;
    }

    // Почему ещё нельзя выбрать ("" - можно)
    static string Problem()
    {
        if (Chosen != PathKind.None) return "Путь уже выбран";
        BuildingInstance hall = Hall();
        if (hall == null || hall.level < RequiredHallLevel) return $"Нужна Цитадель Ур.{RequiredHallLevel}";
        var rm = ResourceManager.Instance;
        if (rm == null || !rm.HasEnough(CostGold, CostWood, CostStone, 0)) return $"Нужно {CostGold} зол., {CostWood} дер., {CostStone} камня";
        return "";
    }

    // Почему ещё нельзя выбрать школу ("" - можно)
    static string SchoolProblem()
    {
        if (!IsMagic) return "Только для пути Магии";
        if (School != MagicSchool.None) return "Школа уже выбрана";
        BuildingInstance hall = Hall();
        if (hall == null || hall.level < SchoolHallLevel) return $"Нужна Цитадель Ур.{SchoolHallLevel}";
        var rm = ResourceManager.Instance;
        if (rm == null || rm.gold < SchoolGold) return $"Нужно {SchoolGold} золота";
        if (rm.herbs < SchoolHerbs) return $"Нужно {SchoolHerbs} трав";
        return "";
    }

    static string EngProblem()
    {
        if (!IsEngineering) return "Только для пути Инженерии";
        if (Eng != EngSchool.None) return "Направление уже выбрано";
        BuildingInstance hall = Hall();
        if (hall == null || hall.level < SchoolHallLevel) return $"Нужна Цитадель Ур.{SchoolHallLevel}";
        var rm = ResourceManager.Instance;
        if (rm == null || rm.gold < EngGold) return $"Нужно {EngGold} золота";
        if (rm.parts < EngParts) return $"Нужно {EngParts} деталей";
        return "";
    }

    void ChooseEng(EngSchool k)
    {
        if (EngProblem() != "" || k == EngSchool.None) return;
        var rm = ResourceManager.Instance;
        if (!rm.Spend(EngGold, 0, 0, 0)) return;
        if (!rm.SpendParts(EngParts)) { rm.Add(EngGold, 0, 0, 0); return; }
        Eng = k;
        confirmEng = EngSchool.None;
        WarManager.Notify($"Инженеры выбрали направление: {EngName(k)}!", true);
    }

    static string WarProblem()
    {
        if (!IsWar) return "Только для пути Военного дела";
        if (War != WarSchool.None) return "Ветка уже выбрана";
        BuildingInstance hall = Hall();
        if (hall == null || hall.level < SchoolHallLevel) return $"Нужна Цитадель Ур.{SchoolHallLevel}";
        var rm = ResourceManager.Instance;
        if (rm == null || rm.gold < WarGold) return $"Нужно {WarGold} золота";
        if (rm.iron < WarIron) return $"Нужно {WarIron} руды";
        return "";
    }

    void ChooseWar(WarSchool k)
    {
        if (WarProblem() != "" || k == WarSchool.None) return;
        var rm = ResourceManager.Instance;
        if (rm.iron < WarIron || !rm.Spend(WarGold, 0, 0, 0)) return;
        rm.iron -= WarIron;
        War = k;
        confirmWar = WarSchool.None;
        WarManager.Notify($"Военное дело: выбрана ветка «{WarName(k)}»!", true);
    }

    /// <summary>Путь, школа, направление и ветка из сохранённого города (без цены и сообщений).</summary>
    public static void RestoreSaved(PathKind path, MagicSchool school, EngSchool eng, WarSchool war)
    {
        Chosen = path == PathKind.Unknown ? PathKind.None : path;
        School = Chosen == PathKind.Magic ? school : MagicSchool.None;
        Eng = Chosen == PathKind.Engineering ? eng : EngSchool.None;
        War = Chosen == PathKind.War ? war : WarSchool.None;
    }

    public static void AdminSetWar(WarSchool k)
    {
        if (k == War) return;
        WarSchool was = War;
        War = k;
        if (Instance != null) Instance.confirmWar = WarSchool.None;
        WarManager.Notify(k == WarSchool.None ? $"[Админ] Ветка сброшена (была: {WarName(was)})"
            : $"[Админ] Ветка: {WarName(was)} → {WarName(k)}", true);
    }

    public static void AdminSetEng(EngSchool k)
    {
        if (k == Eng) return;
        EngSchool was = Eng;
        Eng = k;
        if (Instance != null) Instance.confirmEng = EngSchool.None;
        WarManager.Notify(k == EngSchool.None ? $"[Админ] Направление сброшено (было: {EngName(was)})"
            : $"[Админ] Направление: {EngName(was)} → {EngName(k)}", true);
    }

    void ChooseSchool(MagicSchool k)
    {
        if (SchoolProblem() != "" || k == MagicSchool.None) return;
        var rm = ResourceManager.Instance;
        if (!rm.Spend(SchoolGold, 0, 0, 0)) return;
        if (!rm.SpendHerbs(SchoolHerbs)) { rm.Add(SchoolGold, 0, 0, 0); return; }
        School = k;
        confirmSchool = MagicSchool.None;
        WarManager.Notify($"Маги выбрали школу: {SchoolName(k)}!", true);
    }

    /// <summary>Админ: сменить школу бесплатно (для проверки).</summary>
    public static void AdminSetSchool(MagicSchool k)
    {
        if (k == School) return;
        MagicSchool was = School;
        School = k;
        if (Instance != null) Instance.confirmSchool = MagicSchool.None;
        WarManager.Notify(k == MagicSchool.None ? $"[Админ] Школа сброшена (была: {SchoolName(was)})"
            : $"[Админ] Школа: {SchoolName(was)} → {SchoolName(k)}", true);
    }

    void Choose(PathKind k)
    {
        if (Problem() != "" || k == PathKind.None || k == PathKind.Unknown) return;
        var rm = ResourceManager.Instance;
        if (!rm.Spend(CostGold, CostWood, CostStone, 0)) return;
        Chosen = k;
        confirm = PathKind.None;
        WarManager.Notify($"Выбран путь: {PathName(k)}! Новые постройки — в окне «Постройки»", true);
    }

    /// <summary>
    /// Админ: сменить путь прямо во время игры, бесплатно и без условий (для проверки и отладки).
    /// Здания и войска старого пути остаются, но новые здания/войска старого пути станут недоступны.
    /// </summary>
    public static void AdminSetPath(PathKind k)
    {
        if (k == PathKind.Unknown || k == Chosen) return;
        PathKind was = Chosen;
        Chosen = k;
        if (k != PathKind.Magic) School = MagicSchool.None;
        if (k != PathKind.Engineering) Eng = EngSchool.None;
        if (k != PathKind.War) War = WarSchool.None;
        if (Instance != null) Instance.confirm = PathKind.None;
        WarManager.Notify(k == PathKind.None
            ? $"[Админ] Путь сброшен (был: {PathName(was)}). Можно выбрать заново"
            : $"[Админ] Путь сменён: {PathName(was)} → {PathName(k)}", true);
    }

    void OnGUI()
    {
        if (WarView.IsOpen) return;   // открыта карта клановой войны
        UiKit.Apply();
        windowShown = false;

        bool schoolReady = (IsMagic && School == MagicSchool.None && SchoolProblem() == "")
                        || (IsEngineering && Eng == EngSchool.None && EngProblem() == "")
                        || (IsWar && War == WarSchool.None && WarProblem() == "");
        bool ready = (Chosen == PathKind.None && Problem() == "") || schoolReady;
        string label = Chosen == PathKind.None ? (ready ? "✦ Путь  (!)" : "✦ Путь") : "✦ " + PathName(Chosen) + (schoolReady ? " (!)" : "");
        string tip = Chosen != PathKind.None
            ? $"Путь развития: {PathName(Chosen)}" + (IsMagic ? $"\nШкола: {SchoolName(School)}" : "") + (IsEngineering ? $"\nНаправление: {EngName(Eng)}" : "") + (IsWar ? $"\nВетка: {WarName(War)}" : "")
              + (schoolReady ? (IsMagic ? "\nМожно выбрать школу магии!" : IsWar ? "\nМожно выбрать ветку военного дела!" : "\nМожно выбрать направление инженеров!") : "")
            : (ready ? "Можно выбрать путь развития!" : $"Путь развития: {Problem()}");
        if (open && UiKit.Behind("path")) open = false;   // открыли другое большое окно
        GUIStyle bst = UiKit.ButtonStyle(ready ? UiKit.Kind.Primary : UiKit.Kind.Normal);
        buttonW = Mathf.Clamp(bst.CalcSize(Loc.Content(label)).x + 4f, 104f, 220f);
        if (UiKit.Button(ButtonRect, label, ready ? UiKit.Kind.Primary : UiKit.Kind.Normal, tip)) { open = !open; confirm = PathKind.None; confirmSchool = MagicSchool.None; confirmEng = EngSchool.None; confirmWar = WarSchool.None; if (open && schoolReady) tab = 1; if (open) UiKit.Front("path"); }

        if (!open) return;
        DrawWindow();
    }

    // Коротко (видно на карточке) и полностью (под «Подробнее»)
    const string MagicDesc =
        "Жители учатся работать с травами и маной. Магия не ломает стены, зато ослабляет врагов издалека: ядом, замедлением, огнём и водой. Чем больше маны в воздухе, тем она сильнее.";
    static readonly string[] MagicOpens =
    {
        "<b>Зельеварня</b>: зелья яда и замедления",
        "<b>Отравленные луки</b> в Оружейной",
        "<b>Маг-отравитель</b> · казарма Ур.2",
        "<b>Маг стихий</b> · казарма Ур.3",
        "<b>Магическая оружейная</b>: снаряды для пушек",
        "<b>Школа магии</b> · Цитадель Ур.3",
    };
    const string MagicFull =
        "• Собиратели (Станция Ур.2+) ходят в лес за <b>травами</b>\n" +
        "• <b>Зельеварня</b>: из трав варит <b>зелье отравления</b>\n" +
        "• Оружейная: <b>отравленные луки</b> (зелье на 30 выстрелов)\n" +
        "• Казарма Ур.2: <b>Маг-отравитель</b> — бьёт ядом по площади\n" +
        "• Травы + смола: <b>зелье замедления</b> для луков\n" +
        "• Казарма Ур.3: <b>Маг стихий</b> — снаряды, иногда <b>огненный шар</b>, у воды может <b>затопить</b> врагов\n" +
        "• Цитадель Ур.3: <b>Магическая оружейная</b> — <b>огненные, ледяные и чародейские снаряды</b> для пушек\n" +
        "• Цитадель Ур.3: <b>школа магии</b> на выбор — природа, жизнь или хаос (вкладка «Школа магии» в этом окне)\n" +
        "<color=#8CCCFF>Сила яда и скорость варки зависят от маны в воздухе</color>";

    const string WarDesc =
        "Ставка на мастерство и сапёрное дело. Оружие доводят вручную до ★★★, ловушки ставят прямо посреди боя, через реки наводят понтоны. Путь для тех, кто любит вести бой сам.";
    static readonly string[] WarOpens =
    {
        "<b>Кузня мастера</b>: заточка оружия до ★★★",
        "<b>Боевые ловушки</b>, которые ставят в бою",
        "<b>Понтоны</b> через реки",
        "<b>Поджигатель</b> · казарма Ур.3",
        "<b>Полевая пушка</b> · казарма Ур.3",
        "<b>Ветка военного дела</b> · Цитадель Ур.3",
    };
    const string WarFull =
        "• <b>Кузня мастера</b>: доводка готового оружия из Оружейной\n" +
        "• Мини-игра «точный удар»: три удара по наковальне — <b>заточка</b> (урон), " +
        "<b>закалка</b> (прочность), <b>баланс</b> (скорость удара)\n" +
        "• Чем точнее попадание в центр, тем лучше оружие (до ★★★)\n" +
        "• Казарма Ур.3: <b>Поджигатель</b> — выплёскивает ведро горящей смолы на толпу врагов\n" +
        "• Кузня, вкладка «Ловушки»: обычную ловушку (купить у инженеров на рынке) " +
        "переделать в <b>боевую</b> — её ставят прямо в бою, в набеге тоже (кроме зелёной зоны врага)\n" +
        "• Кузня: <b>понтоны</b> — переправы через реку [P], солдаты идут по ним цепочкой\n" +
        "• Казарма Ур.3: <b>Полевая пушка</b> — ломает стены, заслоны и здания, осколки ранят толпу";

    const string EngDesc =
        "Город превращается в мастерскую. Из руды и смолы делают детали и резину, а из них собирают модули, которые усиливают башни, мосты и пушки. Сильный путь, но котлы можно перегреть.";
    static readonly string[] EngOpens =
    {
        "<b>Литейная</b>: детали и резина",
        "<b>Инженерный двор</b>: модули и ловушки",
        "<b>Паровой отбойник</b> для башни",
        "<b>Раздвижной мост</b>",
        "<b>Подъёмник</b> и <b>бронебойное ядро</b> для пушек",
        "<b>Направление инженеров</b> · Цитадель Ур.3",
    };
    const string EngFull =
        "• <b>Литейная</b>: из руды и стали отливает <b>детали</b>, из смолы варит <b>резину</b>\n" +
        "• Котлы можно раскочегарить — плавка быстрее, но жар растёт: " +
        "<color=#FF7366>перегрев — авария или пожар</color>\n" +
        "• <b>Инженерный двор</b> (4×4): из деталей собирает модули для построек\n" +
        "• Первый модуль — <b>Паровой отбойник</b> для башни: отбрасывает врагов (лёгких далеко, тяжёлых — чуть-чуть)\n" +
        "• Вкладка «Ловушки»: по рецептам из <b>резины</b> — капканы, шипы, сети, пружины; " +
        "добавки меняют свойства. Ставятся у себя в городе, продаются на рынке\n" +
        "• Модуль для моста: <b>раздвижной мост</b> — в бою поднять, и никто не пройдёт\n" +
        "• Для пушек: <b>подъёмник</b> (стрельба через стены) и <b>бронебойное ядро</b>\n" +
        "• Цитадель Ур.3: <b>направление инженеров</b> — военный инженер, пиротехник или индустриализатор (вкладка в этом окне)";

    void DrawWindow()
    {
        // На невысоком экране окно ниже (карточки сами урезают текст), а не уходит низом за край экрана
        float w = Mathf.Min(900f, UiKit.W - 40f), h = Mathf.Clamp(UiKit.H - 60f, 420f, 680f);
        Rect r = new Rect((UiKit.W - w) / 2f, Mathf.Max(50f, (UiKit.H - h) / 2f), w, h);
        if (UiKit.Phone) { r = UiKit.CenterWindow(900f, 680f); w = r.width; h = r.height; }
        windowRect = r;
        windowShown = true;
        CameraController.BlockZoom();

        Rect c = UiKit.Window(r, "Путь развития", out bool close);
        if (close) { open = false; return; }

        // Вкладки: пути и (для Магии) школа магии
        if (IsMagic || IsEngineering || IsWar)
        {
            string schoolTab = IsMagic
                ? (School != MagicSchool.None ? "Школа: " + SchoolName(School) : "Школа магии" + (SchoolProblem() == "" ? "  (!)" : ""))
                : IsWar
                ? (War != WarSchool.None ? "Ветка: " + WarName(War) : "Ветка военного дела" + (WarProblem() == "" ? "  (!)" : ""))
                : (Eng != EngSchool.None ? "Направление: " + EngName(Eng) : "Направление инженеров" + (EngProblem() == "" ? "  (!)" : ""));
            int nt = UiKit.Tabs(new Rect(c.x, c.y, Mathf.Min(520f, c.width), 26f), new[] { "Пути развития", schoolTab }, tab);
            if (nt != tab) { tab = nt; confirm = PathKind.None; confirmSchool = MagicSchool.None; confirmEng = EngSchool.None; confirmWar = WarSchool.None; }
            c = new Rect(c.x, c.y + 34f, c.width, c.height - 34f);
        }
        else tab = 0;
        if (tab == 1) { if (IsMagic) DrawSchools(r, c); else if (IsWar) DrawWarSchools(r, c); else DrawEngSchools(r, c); return; }

        // Шапка: условия или выбранный путь
        string head = Chosen == PathKind.None
            ? $"Цитадель Ур. {RequiredHallLevel}  ·  " + UiKit.Cost(CostGold, CostWood, CostStone, 0) + "   " +
              UiKit.Col(UiKit.WarnHex, "Выбор — навсегда")
            : $"Ваш путь: {UiKit.B(PathName(Chosen))}. Здания — в «Постройках», войска — в казарме.";
        GUIStyle headSt = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        Rect hr = new Rect(c.x + 4f, c.y, c.width - 8f, 22f);
        LocGUI.Label(hr, head, headSt);
        if (Chosen == PathKind.None)
            UiKit.Tip(hr, $"Ячейку направления можно купить, когда Цитадель дорастёт до Ур. {RequiredHallLevel}. " +
                "Путь выбирается один раз: здания и войска других путей станут недоступны.");

        float gap = 12f;
        float cw = (c.width - 2f * gap) / 3f;
        float cy = c.y + 30f, ch = r.yMax - 52f - cy;
        DrawCard(new Rect(c.x, cy, cw, ch), PathKind.Magic, new Color(0.55f, 0.75f, 1f),
            "Магия", "Сила зависит от маны в воздухе", MagicDesc, MagicOpens, MagicFull);
        DrawCard(new Rect(c.x + cw + gap, cy, cw, ch), PathKind.War, new Color(1f, 0.6f, 0.45f),
            "Военное дело", "Мастерство кузнеца и сапёров", WarDesc, WarOpens, WarFull);
        DrawCard(new Rect(c.x + 2f * (cw + gap), cy, cw, ch), PathKind.Engineering, new Color(1f, 0.82f, 0.4f),
            "Инженерия", "Машины, детали, риск перегрева", EngDesc, EngOpens, EngFull);

        // Подтверждение
        if (confirm != PathKind.None)
        {
            Rect cr = new Rect(r.x + w / 2f - 210f, r.yMax - 42f, 420f, 32f);
            if (UiKit.Button(new Rect(cr.x, cr.y, 270f, cr.height), $"Да, навсегда: {PathName(confirm)}", UiKit.Kind.Primary,
                    "Другой путь потом не взять")) Choose(confirm);
            if (UiKit.Button(new Rect(cr.x + 280f, cr.y, 140f, cr.height), "Отмена", UiKit.Kind.Ghost)) confirm = PathKind.None;
        }
        else DrawAdminRow(r);
    }

    // Внизу окна: маленькая кнопка «Админ», раскрывает смену пути без оплаты
    void DrawAdminRow(Rect r)
    {
        if (!GameAdmin.On) return;   // в игре у игроков кнопки «Админ» нет
        float y = r.yMax - 38f;
        if (UiKit.Button(new Rect(r.x + 14f, y, 80f, 26f), adminOpen ? "Админ ▾" : "Админ ▸", UiKit.Kind.Ghost,
                "Для проверки: сменить путь бесплатно, в любой момент")) adminOpen = !adminOpen;
        if (!adminOpen) return;

        PathKind[] kinds = { PathKind.Magic, PathKind.War, PathKind.Engineering, PathKind.None };
        float x = r.x + 104f, bw = Mathf.Min(140f, (r.width - 118f - 3f * 6f) / 4f);
        foreach (var k in kinds)
        {
            string label = k == PathKind.None ? "Сбросить путь" : PathName(k);
            if (UiKit.Button(new Rect(x, y, bw, 26f), label, k != Chosen, k == PathKind.None ? UiKit.Kind.Danger : UiKit.Kind.Normal,
                    k == Chosen ? "Уже выбран" : null)) AdminSetPath(k);
            x += bw + 6f;
        }
    }

    // ---------- Школа магии ----------

    const string NatureDesc =
        "Маги договариваются с лесом и рекой, и своя земля начинает помогать. У воды бойцы сами лечатся, у рек и лесов постройки работают быстрее, а в лесах иногда видно всё, что прячет туман.";
    static readonly string[] NatureOpens =
    {
        "<b>Лечение</b> бойцов у рек",
        "<b>+10%</b> к работе построек у рек и лесов",
        "<b>Взгляд сквозь туман</b> в лесах",
    };
    const string NatureFull =
        "• Свои бойцы рядом с рекой понемногу <b>восстанавливают здоровье</b> (и в бою тоже)\n" +
        "• Постройки рядом с рекой или лесом работают на <b>10% быстрее</b>\n" +
        "• Раз в минуту маги на несколько секунд <b>видят сквозь туман</b> всё, что происходит в лесах: засады, лазутчиков, обходы\n" +
        "<color=#8CCCFF>Сила лечения зависит от маны в воздухе</color>";

    const string LifeDesc =
        "Маги вкладывают в алтарь еду, камень и травы и выращивают из них голема. Голем охраняет всё вокруг алтаря и может пойти с армией в набег. Пока стоит алтарь, голем всегда вернётся.";
    static readonly string[] LifeOpens =
    {
        "<b>Алтарь жизни</b>",
        "<b>Голем</b>-защитник",
        "Голем ходит в <b>набег</b>",
    };
    const string LifeFull =
        "• Новая постройка <b>Алтарь жизни</b> (Цитадель Ур.3)\n" +
        "• В алтарь вкладывают еду, камень и травы — в нём растёт <b>голем</b>\n" +
        "• Голем сам защищает всё вокруг алтаря, но <b>отправить его куда-то нельзя</b>\n" +
        "• В набег его можно взять с армией (галочка в окне похода)\n" +
        "• Алтарь разрушен — голем погибает. Погибший голем растёт заново\n" +
        "<color=#8CCCFF>Голем крепкий, но не сильнее хорошего рыцаря</color>";

    const string ChaosDesc =
        "Самая непредсказуемая школа. Маг стихий получает ядро хаоса и время от времени бросает чёрную дыру: она почти не ранит, но стягивает врагов в кучу под удар пушек, ловушек и огненного шара.";
    static readonly string[] ChaosOpens =
    {
        "<b>Ядро хаоса</b> в Магической оружейной",
        "<b>Чёрная дыра</b> у Мага стихий",
    };
    const string ChaosFull =
        "• В Магической оружейной появляется рецепт <b>ядра хаоса</b>\n" +
        "• Маг стихий сам берёт ядро в своей казарме\n" +
        "• Время от времени маг бросает <b>чёрную дыру</b>: урона мало, зато она <b>стягивает врагов</b> в одну точку — " +
        "удобно под огненный шар, пушку или ловушки\n" +
        "• Ядра хватает на несколько бросков, потом нужно новое\n" +
        "<color=#8CCCFF>Сила дыры зависит от маны в воздухе</color>";

    void DrawSchools(Rect r, Rect c)
    {
        string head = School == MagicSchool.None
            ? $"Цитадель Ур. {SchoolHallLevel}  ·  " + UiKit.Col(UiKit.GoldHex, SchoolGold + " зол") + UiKit.Col(UiKit.MutedHex, " · ") +
              UiKit.Col(UiKit.FoodHex, SchoolHerbs + " трав") + "   " + UiKit.Col(UiKit.WarnHex, "Выбор — навсегда")
            : $"Школа ваших магов: {UiKit.B(SchoolName(School))}.";
        GUIStyle headSt = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(c.x + 4f, c.y, c.width - 8f, 22f), head, headSt);

        float gap = 12f;
        float cw = (c.width - 2f * gap) / 3f;
        float cy = c.y + 30f, ch = r.yMax - 52f - cy;
        DrawSchoolCard(new Rect(c.x, cy, cw, ch), MagicSchool.Nature, new Color(0.5f, 0.9f, 0.5f),
            "Магия природы", "Реки и леса на вашей стороне", NatureDesc, NatureOpens, NatureFull);
        DrawSchoolCard(new Rect(c.x + cw + gap, cy, cw, ch), MagicSchool.Life, new Color(1f, 0.85f, 0.5f),
            "Магия жизни", "Голем, рождённый на алтаре", LifeDesc, LifeOpens, LifeFull);
        DrawSchoolCard(new Rect(c.x + 2f * (cw + gap), cy, cw, ch), MagicSchool.Chaos, new Color(0.8f, 0.5f, 1f),
            "Магия хаоса", "Чёрная дыра в руках мага", ChaosDesc, ChaosOpens, ChaosFull);

        if (confirmSchool != MagicSchool.None)
        {
            Rect cr = new Rect(r.x + r.width / 2f - 210f, r.yMax - 42f, 420f, 32f);
            if (UiKit.Button(new Rect(cr.x, cr.y, 270f, cr.height), $"Да, навсегда: {SchoolName(confirmSchool)}", UiKit.Kind.Primary,
                    "Другую школу потом не взять")) ChooseSchool(confirmSchool);
            if (UiKit.Button(new Rect(cr.x + 280f, cr.y, 140f, cr.height), "Отмена", UiKit.Kind.Ghost)) confirmSchool = MagicSchool.None;
        }
        else DrawSchoolAdminRow(r);
    }

    void DrawSchoolAdminRow(Rect r)
    {
        if (!GameAdmin.On) return;   // в игре у игроков кнопки «Админ» нет
        float y = r.yMax - 38f;
        if (UiKit.Button(new Rect(r.x + 14f, y, 80f, 26f), adminOpen ? "Админ ▾" : "Админ ▸", UiKit.Kind.Ghost,
                "Для проверки: сменить школу бесплатно, в любой момент")) adminOpen = !adminOpen;
        if (!adminOpen) return;

        MagicSchool[] kinds = { MagicSchool.Nature, MagicSchool.Life, MagicSchool.Chaos, MagicSchool.None };
        float x = r.x + 104f, bw = Mathf.Min(140f, (r.width - 118f - 3f * 6f) / 4f);
        foreach (var k in kinds)
        {
            string label = k == MagicSchool.None ? "Сбросить школу" : SchoolName(k);
            if (UiKit.Button(new Rect(x, y, bw, 26f), label, k != School, k == MagicSchool.None ? UiKit.Kind.Danger : UiKit.Kind.Normal,
                    k == School ? "Уже выбрана" : null)) AdminSetSchool(k);
            x += bw + 6f;
        }
    }

    // ---------- Направление инженеров ----------

    const string MilDesc =
        "Инженеры строят осадные машины. Катапульта бросает камни навесом прямо через стены и накрывает целую площадь с огромного расстояния. Она медленная, поэтому держите её за спинами бойцов.";
    static readonly string[] MilOpens =
    {
        "<b>Инженерная казарма</b>",
        "<b>Катапульта</b>",
    };
    const string MilFull =
        "• Новая постройка <b>Инженерная казарма</b> (Цитадель Ур.3)\n" +
        "• Собирает <b>катапульты</b>: нужен один житель-расчёт и материалы (доски, детали)\n" +
        "• Катапульта бьёт камнями <b>по площади</b> очень издалека, через стены\n" +
        "• Урон зависит от расстояния: вблизи сильно, на пределе дальности слабо\n" +
        "• Приказы как у солдат: охранять, идти, стрелять отсюда; ходит в набег";

    const string PyroDesc =
        "Инженеры увлекаются порохом. Башни часто стреляют огненными стрелами, и искры от них ранят соседних врагов. А на земле ждут салюты, которые подбрасывают врага в воздух и поджигают.";
    static readonly string[] PyroOpens =
    {
        "<b>Фейерверочный лук</b> для башни",
        "Ловушка <b>«Салют»</b>",
    };
    const string PyroFull =
        "• Инженерный двор собирает модуль <b>Фейерверочный лук</b> для башни\n" +
        "• Башня стреляет чаще, но каждая стрела слабее — зато в цели взрывается, и <b>искры</b> ранят соседей\n" +
        "• Новая ловушка <b>«Салют»</b>: наступивший взлетает в случайную сторону, в воздухе гремит салют, " +
        "а упав, враг <b>горит</b>\n" +
        "• Ловушка собирается во вкладке «Ловушки» Инженерного двора";

    const string IndDesc =
        "Ставка на производство. Завод создаёт вокруг себя площадку, и все производства на ней работают быстрее, пока есть топливо. Можно разогнать ещё сильнее, но перегрев кончается взрывом.";
    static readonly string[] IndOpens =
    {
        "<b>Завод</b> и заводская площадка",
        "<b>Ускорение</b> производств на топливе",
    };
    const string IndFull =
        "• Новая постройка <b>Завод</b> (Цитадель Ур.3): вокруг него — заводская площадка\n" +
        "• Производства, поставленные на площадке, работают быстрее, пока Завод получает <b>топливо</b> (дерево, уголь-руда)\n" +
        "• Режимы как в Литейной: можно разогнать сильнее, но растёт жар\n" +
        "<color=#FF7366>• Перегрев — взрыв: постройки на площадке повреждены и на время встают</color>";

    void DrawEngSchools(Rect r, Rect c)
    {
        string head = Eng == EngSchool.None
            ? $"Цитадель Ур. {SchoolHallLevel}  ·  " + UiKit.Col(UiKit.GoldHex, EngGold + " зол") + UiKit.Col(UiKit.MutedHex, " · ") +
              UiKit.Col(UiKit.StoneHex, EngParts + " деталей") + "   " + UiKit.Col(UiKit.WarnHex, "Выбор — навсегда")
            : $"Направление ваших инженеров: {UiKit.B(EngName(Eng))}.";
        GUIStyle headSt = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(c.x + 4f, c.y, c.width - 8f, 22f), head, headSt);

        float gap = 12f;
        float cw = (c.width - 2f * gap) / 3f;
        float cy = c.y + 30f, ch = r.yMax - 52f - cy;
        DrawEngCard(new Rect(c.x, cy, cw, ch), EngSchool.Military, new Color(0.85f, 0.7f, 0.5f),
            "Военный инженер", "Осадные машины", MilDesc, MilOpens, MilFull);
        DrawEngCard(new Rect(c.x + cw + gap, cy, cw, ch), EngSchool.Pyro, new Color(1f, 0.55f, 0.35f),
            "Пиротехник", "Огонь, искры и салют", PyroDesc, PyroOpens, PyroFull);
        DrawEngCard(new Rect(c.x + 2f * (cw + gap), cy, cw, ch), EngSchool.Industrial, new Color(0.6f, 0.75f, 0.9f),
            "Индустриализатор", "Заводы и топливо", IndDesc, IndOpens, IndFull);

        if (confirmEng != EngSchool.None)
        {
            Rect cr = new Rect(r.x + r.width / 2f - 210f, r.yMax - 42f, 420f, 32f);
            if (UiKit.Button(new Rect(cr.x, cr.y, 270f, cr.height), $"Да, навсегда: {EngName(confirmEng)}", UiKit.Kind.Primary,
                    "Другое направление потом не взять")) ChooseEng(confirmEng);
            if (UiKit.Button(new Rect(cr.x + 280f, cr.y, 140f, cr.height), "Отмена", UiKit.Kind.Ghost)) confirmEng = EngSchool.None;
            return;
        }

        if (!GameAdmin.On) return;   // в игре у игроков кнопки «Админ» нет
        float y = r.yMax - 38f;
        if (UiKit.Button(new Rect(r.x + 14f, y, 80f, 26f), adminOpen ? "Админ ▾" : "Админ ▸", UiKit.Kind.Ghost,
                "Для проверки: сменить направление бесплатно, в любой момент")) adminOpen = !adminOpen;
        if (!adminOpen) return;
        EngSchool[] kinds = { EngSchool.Military, EngSchool.Pyro, EngSchool.Industrial, EngSchool.None };
        float x = r.x + 104f, bw = Mathf.Min(140f, (r.width - 118f - 3f * 6f) / 4f);
        foreach (var k in kinds)
        {
            string label = k == EngSchool.None ? "Сбросить" : EngName(k);
            if (UiKit.Button(new Rect(x, y, bw, 26f), label, k != Eng, k == EngSchool.None ? UiKit.Kind.Danger : UiKit.Kind.Normal,
                    k == Eng ? "Уже выбрано" : null)) AdminSetEng(k);
            x += bw + 6f;
        }
    }

    // ---------- Ветки военного дела ----------

    const string HeavyDesc =
        "Бойцы идут в бой стеной. Доспехи снижают урон, шипованный заслон ранит тех, кто его бьёт, а заслон на колёсах боец катит перед собой прямо под стрелами.";
    static readonly string[] HeavyOpens =
    {
        "<b>Доспехи</b> в Оружейной",
        "<b>Шипованный заслон</b>",
        "<b>Заслон на колёсах</b>",
    };
    const string HeavyFull =
        "• Оружейная куёт <b>доспехи</b> — снаряжение бойца: меньше урона, чуть медленнее шаг\n" +
        "• Кузня мастера делает <b>шипованный заслон</b>: ранит тех, кто его бьёт\n" +
        "• <b>Заслон на колёсах</b>: боец катит его за собой; в движении он слабее\n" +
        "• Колёсный заслон можно оттолкнуть или притянуть ударами и магией";

    const string BuilderDesc =
        "Пока идёт война, город держится на строителях. Они бегут к отмеченным постройкам и чинят их прямо под ударами, а в поле быстро ставят лёгкие заслоны.";
    static readonly string[] BuilderOpens =
    {
        "<b>Двор военного строителя</b>",
        "<b>Ремонт</b> под огнём",
        "<b>Быстрые заслоны</b>",
    };
    const string BuilderFull =
        "• Новая постройка: <b>Двор военного строителя</b> (Цитадель Ур.3)\n" +
        "• Строители в войну бегут к постройкам, которые вы отметили, и <b>чинят</b> их прямо под ударами\n" +
        "• Ставят <b>быстрые заслоны</b>: меньше и вдвое слабее обычных, но строятся вдвое быстрее и не требуют Кузни\n" +
        "• Помогают достраивать любые заслоны и пункты";

    const string CamoDesc =
        "Война невидимок. Бойцы в накидках из листвы подбираются к врагу незамеченными, а разведчики сидят в дозоре, видят всю округу и сами остаются скрытыми.";
    static readonly string[] CamoOpens =
    {
        "<b>Маскировочные накидки</b>",
        "<b>Разведчики</b> в дозоре",
    };
    const string CamoFull =
        "• Из <b>листвы</b> (Лесопилка Ур.4) шьются накидки — снаряжение бойца\n" +
        "• Пока боец в накидке не атакует, враги замечают его только вдвое ближе\n" +
        "• Разведчик в накидке почти невидим, если не подходит впритык\n" +
        "• Разведчика можно <b>посадить в дозор</b>: стоящего на месте не видит никто, а он видит округу. " +
        "Сидит долго, пока не кончится паёк";

    void DrawWarSchools(Rect r, Rect c)
    {
        string head = War == WarSchool.None
            ? $"Цитадель Ур. {SchoolHallLevel}  ·  " + UiKit.Col(UiKit.GoldHex, WarGold + " зол") + UiKit.Col(UiKit.MutedHex, " · ") +
              UiKit.Col(UiKit.StoneHex, WarIron + " руды") + "   " + UiKit.Col(UiKit.WarnHex, "Выбор — навсегда")
            : $"Ветка военного дела: {UiKit.B(WarName(War))}.";
        GUIStyle headSt = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(c.x + 4f, c.y, c.width - 8f, 22f), head, headSt);

        float gap = 12f;
        float cw = (c.width - 2f * gap) / 3f;
        float cy = c.y + 30f, ch = r.yMax - 52f - cy;
        DrawWarCard(new Rect(c.x, cy, cw, ch), WarSchool.Heavy, new Color(0.75f, 0.75f, 0.8f),
            "Тяжёлая стратегия", "Броня и заслоны", HeavyDesc, HeavyOpens, HeavyFull);
        DrawWarCard(new Rect(c.x + cw + gap, cy, cw, ch), WarSchool.Builder, new Color(0.9f, 0.7f, 0.4f),
            "Военный строитель", "Ремонт под огнём", BuilderDesc, BuilderOpens, BuilderFull);
        DrawWarCard(new Rect(c.x + 2f * (cw + gap), cy, cw, ch), WarSchool.Camo, new Color(0.5f, 0.8f, 0.45f),
            "Мастер маскировки", "Невидимые глаза", CamoDesc, CamoOpens, CamoFull);

        if (confirmWar != WarSchool.None)
        {
            Rect cr = new Rect(r.x + r.width / 2f - 210f, r.yMax - 42f, 420f, 32f);
            if (UiKit.Button(new Rect(cr.x, cr.y, 270f, cr.height), $"Да, навсегда: {WarName(confirmWar)}", UiKit.Kind.Primary,
                    "Другую ветку потом не взять")) ChooseWar(confirmWar);
            if (UiKit.Button(new Rect(cr.x + 280f, cr.y, 140f, cr.height), "Отмена", UiKit.Kind.Ghost)) confirmWar = WarSchool.None;
            return;
        }

        if (!GameAdmin.On) return;   // в игре у игроков кнопки «Админ» нет
        float y = r.yMax - 38f;
        if (UiKit.Button(new Rect(r.x + 14f, y, 80f, 26f), adminOpen ? "Админ ▾" : "Админ ▸", UiKit.Kind.Ghost,
                "Для проверки: сменить ветку бесплатно, в любой момент")) adminOpen = !adminOpen;
        if (!adminOpen) return;
        WarSchool[] kinds = { WarSchool.Heavy, WarSchool.Builder, WarSchool.Camo, WarSchool.None };
        float x = r.x + 104f, bw = Mathf.Min(140f, (r.width - 118f - 3f * 6f) / 4f);
        foreach (var k in kinds)
        {
            string label = k == WarSchool.None ? "Сбросить" : WarName(k);
            if (UiKit.Button(new Rect(x, y, bw, 26f), label, k != War, k == WarSchool.None ? UiKit.Kind.Danger : UiKit.Kind.Normal,
                    k == War ? "Уже выбрано" : null)) AdminSetWar(k);
            x += bw + 6f;
        }
    }

    void DrawWarCard(Rect r, WarSchool k, Color tint, string name, string tagline, string desc, string[] opens, string full)
    {
        bool mine = War == k;
        bool locked = War != WarSchool.None && !mine;
        string p = War == WarSchool.None ? WarProblem() : "";
        string state = mine ? "Ваша ветка" : (locked ? "Недоступно" : null);
        string lockTip = locked ? $"Уже выбрана: {WarName(War)}" : null;
        if (DrawCardBody(r, "war-" + k, tint, name, tagline, desc, opens, full, mine, locked, p, state, lockTip))
            confirmWar = k;
    }

    void DrawEngCard(Rect r, EngSchool k, Color tint, string name, string tagline, string desc, string[] opens, string full)
    {
        bool mine = Eng == k;
        bool locked = Eng != EngSchool.None && !mine;
        string p = Eng == EngSchool.None ? EngProblem() : "";
        string state = mine ? "Ваше направление" : (locked ? "Недоступно" : null);
        string lockTip = locked ? $"Уже выбрано: {EngName(Eng)}" : null;
        if (DrawCardBody(r, "eng-" + k, tint, name, tagline, desc, opens, full, mine, locked, p, state, lockTip))
            confirmEng = k;
    }

    void DrawSchoolCard(Rect r, MagicSchool k, Color tint, string name, string tagline, string desc, string[] opens, string full)
    {
        bool mine = School == k;
        bool locked = School != MagicSchool.None && !mine;
        string p = School == MagicSchool.None ? SchoolProblem() : "";
        string state = mine ? "Ваша школа" : (locked ? "Недоступно" : null);
        string lockTip = locked ? $"Уже выбрана: {SchoolName(School)}" : null;
        if (DrawCardBody(r, "school-" + k, tint, name, tagline, desc, opens, full, mine, locked, p, state, lockTip))
            confirmSchool = k;
    }

    void DrawCard(Rect r, PathKind k, Color tint, string name, string tagline, string desc, string[] opens, string full)
    {
        bool mine = Chosen == k;
        bool locked = k == PathKind.Unknown || (Chosen != PathKind.None && !mine);
        string p = Chosen == PathKind.None && k != PathKind.Unknown ? Problem() : "";
        string state = mine ? "Ваш путь" : (k == PathKind.Unknown ? "Скрыто" : (Chosen != PathKind.None ? "Недоступно" : null));
        string lockTip = Chosen != PathKind.None && !mine ? $"Уже выбран путь: {PathName(Chosen)}" : null;
        if (DrawCardBody(r, "devpath-" + k, tint, name, tagline, desc, opens, full, mine, locked, p, state, lockTip))
            confirm = k;
    }

    /// <summary>
    /// Карточка выбора (путь или школа): описание обычными словами и что открывает выбор.
    /// state != null - вместо «Выбрать» неактивная кнопка с этим текстом.
    /// p - почему пока нельзя выбрать ("" - можно). Возвращает true, если нажали «Выбрать».
    /// </summary>
    bool DrawCardBody(Rect r, string foldId, Color tint, string name, string tagline, string desc, string[] opens, string full,
                      bool mine, bool locked, string p, string state, string lockTip)
    {
        LocGUI.Box(r, GUIContent.none);
        Color old = GUI.color;
        Color top = mine ? UiKit.Good : (locked ? UiKit.Muted : tint);
        GUI.color = new Color(top.r, top.g, top.b, 0.10f);
        GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 34f), Texture2D.whiteTexture);
        GUI.color = top;
        GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 3f), Texture2D.whiteTexture);
        GUI.color = old;

        float x = r.x + 12f, w = r.width - 24f, y = r.y + 8f;
        GUIStyle t = new GUIStyle(UiKit.Title) { alignment = TextAnchor.MiddleCenter };
        t.normal.textColor = locked ? UiKit.Muted : tint;
        LocGUI.Label(new Rect(x, y, w, 24f), mine ? name + "  ✓" : name, t);
        y += 26f;
        GUIStyle tag = new GUIStyle(UiKit.MutedStyle) { alignment = TextAnchor.UpperCenter };
        LocGUI.Label(new Rect(x, y, w, 18f), tagline, tag);
        y += 26f;

        if (locked) GUI.color = new Color(1f, 1f, 1f, 0.55f);

        Rect btn = new Rect(r.x + 10f, r.yMax - 40f, r.width - 20f, 32f);
        bool showWhy = state == null && p != "";
        float foldY = btn.y - 6f - UiKit.FoldH - (showWhy ? 20f : 0f);

        // Строка «Подробнее» над кнопкой: раскрыта - полный список вместо описания
        Rect fr = new Rect(x, foldY, w, UiKit.FoldH);
        bool more = UiKit.Fold(fr, foldId, "Подробнее");
        if (more)
        {
            GUIStyle small = new GUIStyle(UiKit.Small) { fontSize = 12 };
            float fh = Mathf.Min(UiKit.TextH(full, w, small), foldY - 4f - y);
            LocGUI.Label(new Rect(x, y, w, fh), full, small);
        }
        else
        {
            UiKit.Tip(fr, full);
            GUIStyle descSt = new GUIStyle(UiKit.Small) { fontSize = 13 };
            float dh = Mathf.Min(UiKit.TextH(desc, w, descSt), foldY - 4f - y);
            LocGUI.Label(new Rect(x, y, w, dh), desc, descSt);
            y += dh + 10f;

            // «Открывает»: здания, войска и умения, которые появятся после выбора
            if (y + 18f < foldY - 4f)
            {
                LocGUI.Label(new Rect(x, y, w, 18f), "Открывает", UiKit.SectionStyle);
                y += 20f;
            }
            foreach (string o in opens)
            {
                string line = UiKit.Col(mine ? UiKit.GoodHex : UiKit.AccentHex, mine ? "✓ " : "+ ") + o;
                float bh = UiKit.TextH(line, w);
                if (y + bh > foldY - 4f) break;
                LocGUI.Label(new Rect(x, y, w, bh), line, UiKit.Body);
                y += bh + 3f;
            }
        }
        GUI.color = old;

        if (state != null)
        {
            UiKit.Button(btn, state, false, UiKit.Kind.Normal, lockTip);
            return false;
        }
        bool clicked = UiKit.Button(btn, "Выбрать", p == "", UiKit.Kind.Primary, p == "" ? "Выбрать (потребуется подтверждение)" : p);
        if (showWhy)
        {
            GUIStyle why = new GUIStyle(UiKit.MutedStyle) { alignment = TextAnchor.LowerCenter, wordWrap = false };
            why.normal.textColor = UiKit.Warn;
            LocGUI.Label(new Rect(r.x + 6f, btn.y - 20f, r.width - 12f, 18f), p, why);
        }
        return clicked;
    }
}
