using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Рисованная графика игры (картинки лежат в Resources/Art).
///  - Постройки: одна картинка на вид здания, 128 точек = 1 клетка. Если картинки нет - остаётся старый вид.
///  - Человечки: одежда (красится в цвет профессии / отряда), детали поверх (лицо, оружие, обводка) и силуэт для обводки врагов.
///  - Все рисунки людей, коней и повозок смотрят вправо; разворачивает их компонент Facing.
/// </summary>
public static class GameArt
{
    public static bool Enabled = true;

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static readonly HashSet<string> missing = new HashSet<string>();

    static readonly Dictionary<string, string> buildingIds = new Dictionary<string, string>
    {
        { "Ратуша", "townhall" },
        { PopulationManager.HouseName, "house" },
        { BuildingInstance.SawmillName, "sawmill" },
        { BuildingInstance.MineName, "mine" },
        { BuildingInstance.FarmName, "farm" },
        { "Мастерская", "workshop" },
        { BarracksController.TypeName, "barracks" },
        { "Башня", "tower" },
        { BuildingInstance.WallName, "wall" },
        { BuildingInstance.GateName, "gate" },
        { BuildingInstance.BridgeName, "bridge" },
        { BuildingInstance.RoadName, "road" },
        { BuildingInstance.CobbleName, "cobble" },
        { GroundCannonController.TypeName, "cannon" },
        { MagicCannonController.TypeName, "magic_cannon" },
        { MagicArmory.TypeName, "magic_armory" },
        { LifeAltar.TypeName, "life_altar" },
        { EngineerBarracks.TypeName, "eng_barracks" },
        { IndustrialPlant.TypeName, "plant" },
        { WarBuilderYard.TypeName, "warbuilder" },
        { BuildingInstance.GuestHouseName, "guesthouse" },
        { BerryStation.TypeName, "berry" },
        { KitchenController.TypeName, "kitchen" },
        { PotionHouse.TypeName, "potion" },
        { WeaponForge.TypeName, "forge" },
        { FoundryController.TypeName, "foundry" },
        { EngineerYard.TypeName, "eng_yard" },
        { MarketHouse.TypeName, "market" },
        { WagonYard.TypeName, "wagonyard" },
        { OutpostController.TypeName, "outpost" },
        { CourierHouse.TypeName, "courier" },
        { WarehouseController.TypeName, "warehouse" },
        { ArmoryController.TypeName, "armory" },
        { StableController.TypeName, "stable" },
        { HealerHouse.TypeName, "healer" },
    };

    /// <summary>Картинка из Resources/Art/{path}.png. ppu - сколько точек картинки в одной клетке мира.</summary>
    public static Sprite Get(string path, float ppu) { return GetPivot(path, ppu, new Vector2(0.5f, 0.5f)); }

    public static Sprite GetPivot(string path, float ppu, Vector2 pivot)
    {
        if (!Enabled || string.IsNullOrEmpty(path)) return null;
        string key = path + "@" + ppu + "@" + pivot.x + "," + pivot.y;
        Sprite s;
        // Картинки, созданные в прошлом запуске Play, Unity уже удалил (перезагрузка домена выключена) - создаём заново
        if (cache.TryGetValue(key, out s) && s != null) return s;
        if (missing.Contains(path)) return null;

        Texture2D tex = Resources.Load<Texture2D>("Art/" + path);
        if (tex == null) { missing.Add(path); return null; }
        tex.wrapMode = TextureWrapMode.Clamp;
        s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
        s.name = path;
        cache[key] = s;
        return s;
    }

    // ---------- Постройки ----------

    /// <summary>Картинка здания для уровня (если для этого уровня своей нет - берём ближайшую младшую).</summary>
    public static Sprite Building(string typeName, int level = 1)
    {
        string id;
        if (typeName == null || !buildingIds.TryGetValue(typeName, out id)) return null;
        return ById(id, level);
    }

    static Sprite ById(string id, int level)
    {
        for (int lv = Mathf.Clamp(level, 1, 4); lv >= 1; lv--)
        {
            Sprite s = Get("b/" + id + "_" + lv, 128f);
            if (s != null) return s;
        }
        return null;
    }

    /// <summary>Уровень деревни на карте набега (от него зависит, какими рисовать её дома).</summary>
    public static int RaidLevel = 1;

    public static bool HasBuilding(string typeName) { return Building(typeName) != null; }

    /// <summary>Цвет для спрайта здания: у нарисованных - без подкраски, у остальных - цвет вида.</summary>
    public static Color Tint(BuildingType t)
    {
        if (t == null) return Color.white;
        return HasBuilding(t.name) ? Color.white : t.color;
    }

    /// <summary>Поставить картинку здания на его спрайт (до подгонки под клетки).</summary>
    public static void DressBuilding(GameObject b, BuildingType t, int level = 1)
    {
        if (b == null || t == null) return;
        Sprite s = Building(t.name, level);
        if (s == null) return;
        SpriteRenderer sr = b.GetComponentInChildren<SpriteRenderer>();
        if (sr == null) return;
        sr.sprite = s;
        sr.flipX = sr.flipY = false;
        sr.color = Color.white;
        sr.drawMode = SpriteDrawMode.Simple;
        sr.transform.localRotation = Quaternion.identity;
        // Город уже стоит - значит, здание только что построили: оно «вырастает» с пылью и искрами
        bool fresh = BuildingPlacer.Instance != null && BuildingPlacer.Instance.WorldReady;
        ArtBuildingFx.Attach(sr, fresh);
    }

    public static Sprite Raid(RaidBuildingKind kind)
    {
        switch (kind)
        {
            case RaidBuildingKind.Hall: return ById("townhall", RaidLevel);
            case RaidBuildingKind.House: return ById("house", RaidLevel);
            case RaidBuildingKind.Farm: return ById("farm", RaidLevel);
            case RaidBuildingKind.Sawmill: return ById("sawmill", RaidLevel);
            case RaidBuildingKind.Mine: return ById("mine", RaidLevel);
            case RaidBuildingKind.Warehouse: return ById("warehouse", RaidLevel);
            case RaidBuildingKind.Workshop: return ById("workshop", RaidLevel);
            case RaidBuildingKind.Barracks: return ById("barracks", RaidLevel);
            case RaidBuildingKind.Kitchen: return ById("kitchen", RaidLevel);
            case RaidBuildingKind.Stable: return ById("stable", RaidLevel);
            case RaidBuildingKind.Tower: return ById("tower", RaidLevel);
            case RaidBuildingKind.Cannon: return ById("magic_cannon", RaidLevel);
            case RaidBuildingKind.Wall: return ById("wall", RaidLevel);
            case RaidBuildingKind.Gate: return ById("gate", RaidLevel);
            case RaidBuildingKind.Barricade: return ById("barricade", RaidLevel);
            case RaidBuildingKind.Gun: return ById("cannon", RaidLevel);
        }
        return null;
    }

    // ---------- Человечки ----------

    public const string Civilian = "civilian";
    public const string Golem = "golem";      // каменный голем Алтаря жизни

    // Рисунок человечка 128 точек, а клетка туловища - 110: человечек чуть крупнее старого квадратика
    const float FigurePpu = 110f;

    /// <summary>Какой рисунок у этого вида войск (null - рисунка нет, остаётся квадратик).</summary>
    public static string FigureKind(UnitType u)
    {
        if (u == null) return Civilian;
        string n = u.unitName ?? "";
        if (n == LifeAltar.GolemName) return Golem;
        if (n == BarracksController.FieldCannonName || n == BarracksController.CatapultName) return Civilian;
        if (n == RaidManager.ScoutUnitName) return "scout";
        if (n == BarracksController.FireBomberName) return "bomber";
        if (n.Contains("Рыцарь")) return "knight";
        if (n.Contains("аг")) return "mage";          // «Полевой маг», «Маг-отравитель», «Маг стихий»
        if (n.Contains("учник")) return "archer";     // «Лучник», «Конный лучник»
        if (u.IsHealer) return "mage";
        if (u.attackRange > 2f) return "archer";
        return "sword";
    }

    public static Sprite Cloth(string kind) { return kind == null ? null : Get("c/" + kind + "_cloth", FigurePpu); }
    public static Sprite Detail(string kind) { return kind == null ? null : Get("c/" + kind + "_detail", FigurePpu); }
    public static Sprite Outline(string kind) { return kind == null ? null : Get("c/" + kind + "_sil", FigurePpu); }
    /// <summary>Обводка всадника вместе с конём (холст 256x256, центр - центр человечка; null - картинки нет).</summary>
    public static Sprite OutlineRide(string kind) { return kind == null ? null : Get("c/" + kind + "_sil_ride", FigurePpu); }
    public static Sprite Horse() { return Get("c/horse", FigurePpu); }
    /// <summary>Кадр шага: 1 и 2 - ноги врозь (если таких кадров нет - null).</summary>
    public static Sprite DetailStep(string kind, int phase) { return kind == null ? null : Get("c/" + kind + "_detail_w" + phase, FigurePpu); }
    /// <summary>Верхом: ноги согнуты, человечек сидит в седле (null - такой картинки нет).</summary>
    public static Sprite DetailRide(string kind) { return kind == null ? null : Get("c/" + kind + "_detail_ride", FigurePpu); }
    public static Sprite HorseStep(int phase) { return phase == 0 ? Horse() : Get("c/horse_w" + phase, FigurePpu); }

    // ---------- Анимация зданий ----------
    public static Sprite FxPuff() { return Get("c/fx_puff", 64f); }
    /// <summary>Мягкое светлое пятно диаметром 1 клетка (свет в окнах, светлячки).</summary>
    public static Sprite FxGlow() { return Get("c/fx_glow", 64f); }
    /// <summary>Звёздочка-искра диаметром 1 клетка.</summary>
    public static Sprite FxSpark() { return Get("c/fx_spark", 48f); }
    /// <summary>Кольцо пыли диаметром 1 клетка.</summary>
    public static Sprite FxRing() { return Get("c/fx_ring", 128f); }
    /// <summary>Кадр развевающегося флажка (color: r - красный, y - жёлтый; frame 1-4), 1 клетка в длину, точка - у древка.</summary>
    public static Sprite FxFlag(string color, int frame) { return GetPivot("c/fx_flag_" + color + "_" + frame, 64f, new Vector2(2f / 64f, 0.5f)); }
    /// <summary>Таран сбоку (смотрит вправо): бревно с окованной головой, 1 клетка в длину.</summary>
    public static Sprite Ram() { return Get("c/ram", 256f); }

    /// <summary>
    /// Нарисовать таран на месте квадратиков «бревно» и «голова» (RaidUnit, EnemyAgent): бревно получает картинку,
    /// голова прячется (она уже на картинке). bodyScale - размер туловища носильщика. Картинки нет - остаются квадратики.
    /// </summary>
    public static void DressRam(SpriteRenderer log, SpriteRenderer head, float bodyScale)
    {
        Sprite s = Ram();
        if (s == null || log == null) return;
        log.sprite = s;
        log.color = Color.white;
        log.transform.localPosition = new Vector3(bodyScale * 0.05f, -bodyScale * 0.15f, 0f);
        log.transform.localScale = new Vector3(bodyScale * 2.6f, bodyScale * 2.6f, 1f);
        if (head != null) head.sprite = null;
    }
    public static Sprite FxMill() { return Get("c/fx_mill", 128f); }
    public static Sprite FxSaw() { return Get("c/fx_saw", 128f); }
    /// <summary>Ствол пушки (смотрит вправо): 1 клетка в длину, 1/4 в толщину.</summary>
    public static Sprite FxBarrel() { return Get("c/fx_barrel", 128f); }

    /// <summary>Лафет-вертлюг наземной пушки (вид сверху, крутится вместе со стволом): 1 клетка.</summary>
    public static Sprite FxMount() { return Get("c/fx_mount", 128f); }
    /// <summary>Подъёмник наземной пушки (вид сверху): опущен / поднят. 1 клетка.</summary>
    public static Sprite FxLift(bool up) { return Get(up ? "c/fx_lift_up" : "c/fx_lift", 128f); }

    /// <summary>
    /// Лафет полевой пушки сбоку (смотрит вправо), 128x96 точек: 0 - простой, 1 - со сложенным подъёмником, 2 - поднят.
    /// Ствол кладут на цапфу: CarriageTrunnion - где она от центра картинки (в долях ширины картинки, y вверх).
    /// </summary>
    public static Sprite FxCarriage(int state)
    {
        return Get(state >= 2 ? "c/fx_carriage_up" : state == 1 ? "c/fx_carriage_jack" : "c/fx_carriage", 128f);
    }
    public static Vector2 CarriageTrunnion(int state) { return new Vector2(10f / 128f, (state >= 2 ? 23f : -7f) / 128f); }

    // ---------- Стены и ворота: соединяются во все стороны ----------
    // Соседи стены (стены, ворота, башни) - маской: 1 север, 2 восток, 4 юг, 8 запад.
    public const int JoinN = 1, JoinE = 2, JoinS = 4, JoinW = 8;

    /// <summary>Кусок стены под соседей (прямой, угол, развилка, конец). null - таких картинок нет.</summary>
    public static Sprite WallJoined(int level, int mask)
    {
        for (int lv = Mathf.Clamp(level, 1, 3); lv >= 1; lv--)
        {
            Sprite s = Get("b/wall_" + lv + "_j" + (mask & 15), 128f);
            if (s != null) return s;
        }
        return null;
    }

    /// <summary>Ворота стоят в стене, идущей вверх-вниз.</summary>
    public static bool GateVertical(int mask) { return (mask & (JoinN | JoinS)) != 0 && (mask & (JoinE | JoinW)) == 0; }

    /// <summary>Ворота: поперёк или вдоль, закрытые или распахнутые.</summary>
    public static Sprite GateLook(int level, bool vertical, bool open)
    {
        string k = (vertical ? "v" : "h") + (open ? "o" : "");
        for (int lv = Mathf.Clamp(level, 1, 3); lv >= 1; lv--)
        {
            Sprite s = Get("b/gate_" + lv + "_" + k, 128f);
            if (s != null) return s;
        }
        return null;
    }

    public struct AnimSpot { public string kind; public float x, y, size; }
    static Dictionary<string, List<AnimSpot>> anim;
    static readonly List<AnimSpot> noSpots = new List<AnimSpot>();

    /// <summary>Где на картинке (в точках от центра, y вверх) дымят трубы и крутятся колёса.</summary>
    public static List<AnimSpot> AnimSpots(string spriteName)
    {
        if (anim == null)
        {
            anim = new Dictionary<string, List<AnimSpot>>();
            // anim.txt пишет рисовалка зданий; anim_vizual.txt - флажки и свечение (vizual-2)
            foreach (string file in new[] { "Art/anim", "Art/anim_vizual" })
            {
            TextAsset ta = Resources.Load<TextAsset>(file);
            if (ta != null)
                foreach (string raw in ta.text.Split('\n'))
                {
                    string[] f = raw.Trim().Split(' ');
                    if (f.Length < 5) continue;
                    AnimSpot s = new AnimSpot { kind = f[1] };
                    float.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.x);
                    float.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.y);
                    float.TryParse(f[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.size);
                    string key = "b/" + f[0];
                    List<AnimSpot> list;
                    if (!anim.TryGetValue(key, out list)) anim[key] = list = new List<AnimSpot>();
                    list.Add(s);
                }
            }
        }
        List<AnimSpot> res;
        return spriteName != null && anim.TryGetValue(spriteName, out res) ? res : noSpots;
    }

    // ---------- Лес и кусты ----------
    /// <summary>Дерево (0-2 лиственные, 3-4 ели): крона диаметром в 1 клетку, точка - центр кроны.</summary>
    public static Sprite Tree(int variant)
    {
        string n = variant < 3 ? "n/tree_" + (variant + 1) : "n/pine_" + (variant - 2);
        return GetPivot(n, 112f, new Vector2(72f / 160f, 1f - 72f / 160f));
    }
    public static Sprite Fern(int variant) { return Get("n/fern_" + (variant + 1), 96f); }
    public static Sprite BushLeaves() { return GetPivot("n/bush", 88f, new Vector2(60f / 128f, 1f - 60f / 128f)); }
    public static Sprite Berries() { return Get("n/berries", 72f); }
    public static Sprite Shadow() { return Get("c/shadow", 64f); }
    public static Sprite Armor() { return Get("c/gear_armor", FigurePpu); }
    public static Sprite Cloak() { return Get("c/gear_cloak", FigurePpu); }
    /// <summary>Повозка с лошадью: 300x150 точек, около 1.25 клетки в длину.</summary>
    public static Sprite Cart() { return Get("c/cart", 240f); }
    /// <summary>Насколько сдвинуть картинку повозки, чтобы кузов оказался в центре (лошадь впереди, справа).</summary>
    public const float CartShift = 0.27f;

    /// <summary>Конь под всадником: картинку ставим под туловищем так, чтобы седло было на поясе.</summary>
    public static void PlaceHorse(SpriteRenderer horse, float bodyScale)
    {
        if (horse == null) return;
        Sprite s = Horse();
        if (s == null) return;
        horse.sprite = s;
        horse.color = Color.white;
        horse.transform.localPosition = new Vector3(0f, -bodyScale * 0.42f, 0f);
        horse.transform.localScale = new Vector3(bodyScale * 1.05f, bodyScale * 1.05f, 1f);
    }

    /// <summary>
    /// Нарядить квадратик-туловище в человечка: туловище получает рисунок одежды (его цвет по-прежнему задаёт игра),
    /// поверх - детали, снизу - тень. outline (если есть) становится силуэтом-обводкой.
    /// </summary>
    public static ArtFigure Dress(SpriteRenderer body, string kind, SpriteRenderer outline = null)
    {
        if (body == null || Cloth(kind) == null) return null;
        ArtFigure f = body.GetComponent<ArtFigure>();
        if (f == null) f = body.gameObject.AddComponent<ArtFigure>();
        f.Setup(body, outline);
        f.SetKind(kind);
        return f;
    }
}
