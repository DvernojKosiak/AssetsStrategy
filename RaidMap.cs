using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Карта набега: отдельное поле боя далеко в стороне от нашей деревни (центр - Origin).
/// Своя сетка проходимости и поиск пути (A*), чтобы не мешать навигации города (GridNavigation).
/// Клетки - локальные: от -Size/2 до Size/2-1, мировая точка = Origin + (x + 0.5, y + 0.5).
/// Размер - как у нашей карты (BuildingPlacer.mapSize), но не меньше MinSize и не больше MaxSize.
/// На карте растут леса (непроходимые, план - в BotVillage.Plan.Trees).
/// Создаётся RaidManager при прибытии отряда и удаляется после похода.
/// </summary>
public class RaidMap : MonoBehaviour
{
    public static RaidMap Instance { get; private set; }

    public const int MinSize = 44, MaxSize = 400;   // большой город игрока - карта набега такая же, не обрезаем

    /// <summary>Пока строится план деревни игрока - размер её карты (0 - нет).</summary>
    public static int SizeOverride;

    /// <summary>
    /// Размер карты набега: у идущего боя - его карта; иначе как у нашей карты (чётный, в пределах MinSize..MaxSize).
    /// Деревня игрока - размер её карты (SizeOverride / Create(..., mapSize)), чтобы клетки совпадали с её городом.
    /// </summary>
    public static int Size
    {
        get
        {
            if (SizeOverride > 0) return Fit(SizeOverride);
            if (Instance != null && Instance.size > 0) return Instance.size;
            return Fit(GridNavigation.Instance != null ? GridNavigation.Instance.mapSize : 50);
        }
    }

    public static int Fit(int size)
    {
        int s = Mathf.Clamp(size, MinSize, MaxSize);
        return s - (s % 2);
    }

    // Центр поля боя в мире: далеко от нашей карты и от ночного затемнения (оно до 200 клеток от центра)
    public static Vector2 Origin => new Vector2(300f + MaxSize / 2f, 0f);

    // ---------- Сторона высадки (обновление final-1) ----------
    // Вся логика набега (план деревни, клетки, отчёт серверу) считает, что наши высаживаются слева (с запада).
    // Чтобы армия приходила с разных сторон, поворачивается только связь «клетка карты ↔ точка мира»:
    // «запад плана» оказывается на западе, востоке, юге или севере экрана. Клетки и план не меняются.
    // Side: 0 - слева (как раньше), 1 - справа (зеркально), 2 - снизу, 3 - сверху.
    // Сторона известна заранее (RaidTarget.ApproachSide, final-4); в живом бою поворачиваются и точки из снимков.

    public static int Side { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSide() { Side = 0; PlanRot = 0; SizeOverride = 0; }

    public static void SetSide(int side) { Side = Mathf.Clamp(side, 0, 3); }

    public static string SideName(int side)
    {
        switch (side)
        {
            case 1: return "с востока";
            case 2: return "с юга";
            case 3: return "с севера";
            default: return "с запада";
        }
    }

    /// <summary>Сдвиг в координатах плана → сдвиг в мире.</summary>
    public static Vector2 WorldDir(Vector2 f) { return WorldDirFor(Side, f); }

    /// <summary>То же для любой стороны (карта разведчика рисуется так, как армия увидит деревню на месте).</summary>
    public static Vector2 WorldDirFor(int side, Vector2 f)
    {
        // Только повороты (final-8): раньше восток был зеркалом, а юг - отражением по диагонали, и деревня игрока
        // на экране выходила вывернутой, а здания - растянутыми поперёк
        switch (side)
        {
            case 1: return new Vector2(-f.x, -f.y);
            case 2: return new Vector2(-f.y, f.x);
            case 3: return new Vector2(f.y, -f.x);
            default: return f;
        }
    }

    /// <summary>Сдвиг в мире → сдвиг в координатах плана.</summary>
    public static Vector2 FrameDir(Vector2 w)
    {
        return FrameDirFor(Side, w);
    }

    /// <summary>Обратное к WorldDirFor.</summary>
    public static Vector2 FrameDirFor(int side, Vector2 w)
    {
        switch (side)
        {
            case 1: return new Vector2(-w.x, -w.y);
            case 2: return new Vector2(w.y, -w.x);
            case 3: return new Vector2(-w.y, w.x);
            default: return w;
        }
    }

    /// <summary>Угол в мире (градусы) → угол в координатах плана (живой бой: приказы и снимки защитника).</summary>
    public static float FrameAngle(float worldDeg)
    {
        if (Side == 0) return worldDeg;
        float a = worldDeg * Mathf.Deg2Rad;
        Vector2 d = FrameDir(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    /// <summary>Угол в координатах плана → угол в мире.</summary>
    public static float WorldAngle(float frameDeg)
    {
        if (Side == 0) return frameDeg;
        float a = frameDeg * Mathf.Deg2Rad;
        Vector2 d = WorldDir(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    /// <summary>Значок стороны высадки: куда смотрит стрелка, когда армия входит на карту.</summary>
    public static string SideArrow(int side)
    {
        switch (side)
        {
            case 1: return "◄";
            case 2: return "▲";
            case 3: return "▼";
            default: return "►";
        }
    }

    /// <summary>Точка мира → координата плана (не клетка).</summary>
    public static Vector2 FrameOf(Vector2 world) { return FrameDir(world - Origin); }

    /// <summary>Размер прямоугольника плана в мире (снизу и сверху ширина и высота меняются местами).</summary>
    public static Vector2 WorldSize(Vector2 s) { return Side >= 2 ? new Vector2(s.y, s.x) : s; }

    /// <summary>Прямоугольник клеток плана (угол и размер) - в мире.</summary>
    public static Rect WorldRect(Vector2Int origin, Vector2Int size)
    {
        Vector2 a = Local(origin.x, origin.y), b = Local(origin.x + size.x, origin.y + size.y);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // ---------- Деревня игрока: клетки хозяина ↔ клетки плана (final-8) ----------
    // План набега всегда «нападают с запада». Чтобы армия пришла к деревне игрока с настоящей стороны и деревня
    // на экране была такой же, как у хозяина (здания не растянуты и не перевёрнуты), его город поворачивается
    // в план на PlanRot (= сторона подхода), а экран (WorldDir) поворачивает обратно. Боты: PlanRot = 0.
    // Всё, что уходит хозяину (снимки, приказы, руины), - в его клетках (RealOf / LiveOf).

    public static int PlanRot { get; private set; }
    public static void SetPlanRot(int rot) { PlanRot = Mathf.Clamp(rot, 0, 3); }

    /// <summary>Точка хозяина → точка плана (поворот вокруг центра карты).</summary>
    public static Vector2 RealToPlan(int rot, Vector2 r) { return FrameDirFor(rot, r); }
    /// <summary>Точка плана → точка хозяина.</summary>
    public static Vector2 PlanToReal(int rot, Vector2 p) { return WorldDirFor(rot, p); }

    /// <summary>Прямоугольник клеток хозяина (левый нижний угол и размер) → в плане.</summary>
    public static void RealRectToPlan(int rot, Vector2Int o, Vector2Int s, out Vector2Int po, out Vector2Int ps)
    {
        RotRect(o, s, v => RealToPlan(rot, v), out po, out ps);
    }

    /// <summary>Прямоугольник клеток плана → у хозяина.</summary>
    public static void PlanRectToReal(int rot, Vector2Int o, Vector2Int s, out Vector2Int ro, out Vector2Int rs)
    {
        RotRect(o, s, v => PlanToReal(rot, v), out ro, out rs);
    }

    static void RotRect(Vector2Int o, Vector2Int s, System.Func<Vector2, Vector2> f, out Vector2Int ro, out Vector2Int rs)
    {
        Vector2 a = f(new Vector2(o.x, o.y)), b = f(new Vector2(o.x + s.x, o.y + s.y));
        ro = new Vector2Int(Mathf.RoundToInt(Mathf.Min(a.x, b.x)), Mathf.RoundToInt(Mathf.Min(a.y, b.y)));
        rs = new Vector2Int(Mathf.RoundToInt(Mathf.Abs(a.x - b.x)), Mathf.RoundToInt(Mathf.Abs(a.y - b.y)));
    }

    /// <summary>Точка мира на карте набега → точка в клетках хозяина деревни (живой бой: снимки и приказы).</summary>
    public static Vector2 RealOf(Vector2 world) { return PlanToReal(PlanRot, FrameOf(world)); }
    /// <summary>Точка в клетках хозяина → точка мира на карте набега.</summary>
    public static Vector2 WorldOfReal(Vector2 real) { return Local(RealToPlan(PlanRot, real)); }

    /// <summary>Угол в мире → угол у хозяина деревни.</summary>
    public static float RealAngle(float worldDeg)
    {
        float a = worldDeg * Mathf.Deg2Rad;
        Vector2 d = PlanToReal(PlanRot, FrameDir(new Vector2(Mathf.Cos(a), Mathf.Sin(a))));
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    /// <summary>Угол у хозяина деревни → угол в мире.</summary>
    public static float WorldAngleOfReal(float realDeg)
    {
        float a = realDeg * Mathf.Deg2Rad;
        Vector2 d = WorldDir(RealToPlan(PlanRot, new Vector2(Mathf.Cos(a), Mathf.Sin(a))));
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    /// <summary>Левый нижний угол здания плана - в клетках хозяина (shift - сдвиг плана на восток, Plan.Shift).</summary>
    public static Vector2Int RealOrigin(Vector2Int planOrigin, Vector2Int planSize, int shift = 0)
    {
        PlanRectToReal(PlanRot, new Vector2Int(planOrigin.x - shift, planOrigin.y), planSize, out Vector2Int ro, out _);
        return ro;
    }

    readonly HashSet<Vector2Int> trees = new HashSet<Vector2Int>();
    public bool IsTree(Vector2Int c) { return trees.Contains(c); }

    // Местность города игрока: река (вброд, медленно) и лес (проходимый, медленнее)
    readonly HashSet<Vector2Int> water = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> woods = new HashSet<Vector2Int>();
    public bool IsWater(Vector2Int c) { return water.Contains(c); }
    public bool IsWoods(Vector2Int c) { return woods.Contains(c); }

    /// <summary>Во сколько раз медленнее идти здесь (вброд - как дома, по лесу - как дома).</summary>
    public float SpeedMul(Vector2 world, bool mounted)
    {
        if (water.Count == 0 && woods.Count == 0) return 1f;
        Vector2Int c = WorldToCell(world);
        if (water.Contains(c)) return mounted ? Mathf.Max(Rivers.WadeSpeed, 0.6f) : Rivers.WadeSpeed;
        if (woods.Contains(c)) return ResourceZones.ForestSpeed;
        return 1f;
    }

    int Half => size / 2;

    bool[] blocked;
    bool[] pushable;   // заслон на колёсах: пройти можно, только толкая его (A* - дороже, по прямой - нельзя)
    bool dirty = true;
    GameObject background;

    // Буферы A*. Клетку не чистим перед каждым поиском (на карте 400x400 это 160 000 клеток × 3 массива):
    // метка stamp говорит, что g/parent записаны в этом поиске, а closedMark - что клетка уже раскрыта.
    float[] g, f;
    int[] parent;
    int[] seen, closedMark;
    int stamp;

    static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
    };

    // Бюджет поисков пути на кадр (на всех бойцов поля боя): не больше perFrame поисков и не дольше PathMsPerFrame мс.
    // Кому не хватило - ищет в следующем кадре (боец пару кадров стоит, зато игра не замирает).
    const float PathMsPerFrame = 3f;
    static int budgetFrame = -1, budgetUsed;
    static readonly System.Diagnostics.Stopwatch pathClock = new System.Diagnostics.Stopwatch();
    public static bool TryUsePathBudget(int perFrame = 6)
    {
        if (Time.frameCount != budgetFrame) { budgetFrame = Time.frameCount; budgetUsed = 0; pathClock.Reset(); }
        if (budgetUsed >= perFrame) return false;
        if (budgetUsed > 0 && pathClock.Elapsed.TotalMilliseconds >= PathMsPerFrame) return false;
        budgetUsed++;
        return true;
    }

    // Предел раскрытых клеток на один поиск: на большой карте путь «в никуда» (цель за сплошной стеной)
    // раньше обходил всю карту - сотни тысяч клеток. Не нашли за столько - считаем, что пути нет (бойцы идут пробивать стену).
    const int MaxExpand = 24000;

    int size;   // размер этой карты (запоминаем при создании)

    public static RaidMap Create(List<Vector2Int> forest) { return Create(forest, null, null); }

    public static RaidMap Create(List<Vector2Int> forest, List<Vector2Int> river, List<Vector2Int> wood, int mapSize = 0)
    {
        int sz = mapSize > 0 ? Fit(mapSize) : Fit(GridNavigation.Instance != null ? GridNavigation.Instance.mapSize : 50);
        if (Instance != null) Destroy(Instance.gameObject);
        GameObject go = new GameObject("RaidMap");
        go.transform.position = new Vector3(Origin.x, Origin.y, 0f);
        RaidMap m = go.AddComponent<RaidMap>();
        Instance = m;
        m.size = sz;
        if (forest != null) foreach (var c in forest) m.trees.Add(c);
        if (river != null) foreach (var c in river) m.water.Add(c);
        if (wood != null) foreach (var c in wood) if (!m.water.Contains(c)) m.woods.Add(c);
        m.CreateBackground();
        m.PlantWoods();
        return m;
    }

    // Деревья леса игрока (как дома: нарисованные, через них можно пройти)
    void PlantWoods()
    {
        if (woods.Count == 0) return;
        var rng = new System.Random(woods.Count * 31 + size);
        foreach (var c in woods)
        {
            if (!InBounds(c) || rng.NextDouble() < 0.12) continue;
            GameObject go = new GameObject("Tree");
            go.transform.SetParent(transform, false);
            float s = 0.55f + (float)rng.NextDouble() * 0.4f;
            Vector2 tp = Local(c.x + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.6f, c.y + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.6f);
            go.transform.position = new Vector3(tp.x, tp.y, 0f);
            go.transform.localScale = new Vector3(s, s, 1f);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -7;
            Sprite art = GameArt.Tree(rng.NextDouble() < 0.35 ? 3 + rng.Next(2) : rng.Next(3));
            if (art != null) { sr.sprite = art; float v = 0.86f + (float)rng.NextDouble() * 0.14f; sr.color = new Color(v, v, v); }
            else { sr.sprite = CombatFx.Circle(); sr.color = new Color(0.16f, 0.36f, 0.14f); }

            // Подлесок: папоротники (как в лесу города)
            if (rng.NextDouble() < 0.3 && GameArt.Fern(0) != null)
            {
                GameObject fg = new GameObject("Fern");
                fg.transform.SetParent(transform, false);
                Vector2 fp = Local(c.x + 0.15f + (float)rng.NextDouble() * 0.7f, c.y + 0.15f + (float)rng.NextDouble() * 0.7f);
                fg.transform.position = new Vector3(fp.x, fp.y, 0f);
                float fs = 0.45f + (float)rng.NextDouble() * 0.25f;
                fg.transform.localScale = new Vector3(fs, fs, 1f);
                SpriteRenderer fr = fg.AddComponent<SpriteRenderer>();
                fr.sprite = GameArt.Fern(rng.Next(2));
                fr.sortingOrder = -8;
            }
        }
    }

    /// <summary>Дорожка, брусчатка или мост деревни игрока: только картинка на земле (ходить можно, бить нельзя).</summary>
    public void AddDecor(string type, int level, Vector2Int origin, Vector2Int sz, Color color)
    {
        GameObject go = new GameObject("Decor_" + type);
        go.transform.SetParent(transform, false);
        Vector2 dc = Local(origin.x + sz.x / 2f, origin.y + sz.y / 2f);
        go.transform.position = new Vector3(dc.x, dc.y, 0f);
        Vector2 wsz = WorldSize(new Vector2(sz.x, sz.y));   // снизу / сверху - ширина и высота меняются местами
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        bool bridge = type == BuildingInstance.BridgeName;
        sr.sortingOrder = bridge ? -9 : -10;   // как в городе: дорожки ниже деревьев и людей, мост над водой
        Sprite art = GameArt.Building(type, level);
        if (art != null)
        {
            sr.sprite = art;
            Vector2 a = art.bounds.size;
            go.transform.localScale = new Vector3(wsz.x / Mathf.Max(0.01f, a.x), wsz.y / Mathf.Max(0.01f, a.y), 1f);
        }
        else
        {
            sr.sprite = CombatFx.Square();
            sr.color = color;
            go.transform.localScale = new Vector3(wsz.x, wsz.y, 1f);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (background != null) Destroy(background);
    }

    void CreateBackground()
    {
        int Size = size;
        int Half = Size / 2;
        // Трава - как у нас в городе (BuildingPlacer.CreateGridBackground): тот же шум, те же цвета и сетка.
        // При 16 точках на клетку и том же размере карты рисунок совпадает точь-в-точь с городом защитника.
        int cellPx = Size <= 200 ? 16 : 8;
        int res = Size * cellPx;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        BuildingPlacer bp = BuildingPlacer.Instance;
        Color grassDark = bp != null ? bp.grassDark : new Color(0.20f, 0.42f, 0.16f);
        Color grassLight = bp != null ? bp.grassLight : new Color(0.36f, 0.62f, 0.25f);
        bool lines = bp == null || bp.showGridLines;
        float lineK = bp != null ? bp.gridLineStrength : 0.10f;
        float k16 = 16f / cellPx;   // шаг шума - как при 16 точках на клетку

        Color[] px = new Color[res * res];
        var grng = new System.Random(12345);
        float offX = grng.Next(0, 1000), offY = grng.Next(0, 1000);
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float big = Mathf.PerlinNoise(offX + x * k16 * 0.02f, offY + y * k16 * 0.02f);
                float mid = Mathf.PerlinNoise(offX + x * k16 * 0.12f, offY + y * k16 * 0.12f);
                Color c = Color.Lerp(grassDark, grassLight, Mathf.Clamp01(big * 0.65f + mid * 0.35f));
                double rnd = grng.NextDouble();
                if (rnd < 0.06) c = Color.Lerp(c, grassLight * 1.25f, 0.6f);
                else if (rnd < 0.12) c = Color.Lerp(c, grassDark * 0.7f, 0.6f);
                if (rnd > 0.9985) c = new Color(0.95f, 0.9f, 0.4f);
                if (lines && (x % cellPx == 0 || y % cellPx == 0)) c = Color.Lerp(c, Color.black, lineK);
                c.a = 1f;
                px[y * res + x] = c;
            }

        // Полоса высадки у западного края - чуть вытоптана
        for (int y = 0; y < res; y++)
            for (int x = 0; x < 4 * cellPx; x++)
                px[y * res + x] = Color.Lerp(px[y * res + x], new Color(0.55f, 0.5f, 0.35f), 0.18f);

        // Леса: тёмные кроны (по круглому пятну на клетку, оттенок чуть гуляет)
        var rng = new System.Random(trees.Count * 7919 + Size);
        foreach (var t in trees)
        {
            int bx = (t.x + Half) * cellPx, by = (t.y + Half) * cellPx;
            float shade = 0.8f + (float)rng.NextDouble() * 0.3f;
            Color crown = new Color(0.12f * shade, 0.32f * shade, 0.13f * shade);
            Color edge = new Color(0.08f, 0.2f, 0.09f);
            float rad = cellPx * (0.55f + (float)rng.NextDouble() * 0.15f);
            float cx = bx + cellPx / 2f + (float)(rng.NextDouble() - 0.5) * 2f;
            float cy = by + cellPx / 2f + (float)(rng.NextDouble() - 0.5) * 2f;
            for (int x = bx - 2; x < bx + cellPx + 2; x++)
                for (int y = by - 2; y < by + cellPx + 2; y++)
                {
                    if (x < 0 || y < 0 || x >= res || y >= res) continue;
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    if (d > rad) continue;
                    px[y * res + x] = d > rad - 1.2f ? edge : crown;
                }
        }

        // Лесная подстилка под деревьями игрока - как в городе, чуть темнее травы
        foreach (var w in woods)
        {
            if (!InBounds(w)) continue;
            int bx = (w.x + Half) * cellPx, by = (w.y + Half) * cellPx;
            for (int x = bx; x < bx + cellPx; x++)
                for (int y = by; y < by + cellPx; y++)
                    px[y * res + x] = Color.Lerp(px[y * res + x], new Color(0.14f, 0.30f, 0.12f), 0.35f);
        }

        PaintRiver(px, res, cellPx, Half);
        tex.SetPixels(px);
        tex.Apply();

        background = PlainQuad.Create();
        background.name = "RaidMap_Background";
        background.transform.position = new Vector3(Origin.x, Origin.y, 1f);
        background.transform.localScale = new Vector3(Size, Size, 1f);
        // Сторона высадки: картинка земли (нарисована по клеткам плана) поворачивается так же, как клетки
        switch (Side)
        {
            case 1: background.transform.localRotation = Quaternion.Euler(0f, 0f, 180f); break;
            case 2: background.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); break;
            case 3: background.transform.localRotation = Quaternion.Euler(0f, 0f, -90f); break;
        }
        Material mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        Renderer r = background.GetComponent<Renderer>();
        r.material = mat;
        r.sortingOrder = -100;
    }

    // Река как в городе (Rivers.BuildLook): вода плавно между клетками, глубже от берега - темнее,
    // песчаная кромка и тёмная плодородная трава вдоль берега
    void PaintRiver(Color[] px, int res, int cellPx, int Half)
    {
        if (water.Count == 0) return;
        int N = size;
        // Глубина клетки: 1 у берега, дальше от берега - больше (до 3)
        var depth = new int[N * N];
        var bank = new int[N * N];
        var q = new Queue<Vector2Int>();
        foreach (var w in water)
        {
            if (!InBounds(w)) continue;
            bool shore = false;
            for (int dx = -1; dx <= 1 && !shore; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    Vector2Int n = new Vector2Int(w.x + dx, w.y + dy);
                    if (InBounds(n) && !water.Contains(n)) { shore = true; break; }
                }
            if (shore) { depth[Idx(w)] = 1; q.Enqueue(w); }
        }
        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            int dc = depth[Idx(c)];
            foreach (var d in Dirs)
            {
                Vector2Int n = c + d;
                if (!InBounds(n) || !water.Contains(n) || depth[Idx(n)] != 0) continue;
                depth[Idx(n)] = Mathf.Min(3, dc + 1);
                q.Enqueue(n);
            }
        }
        // Берег: 1-2 клетки от воды
        foreach (var w in water)
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    Vector2Int n = new Vector2Int(w.x + dx, w.y + dy);
                    if (!InBounds(n) || water.Contains(n)) continue;
                    int b = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    int i = Idx(n);
                    if (bank[i] == 0 || b < bank[i]) bank[i] = b;
                }

        float Cell(int cx, int cy, bool isWater)
        {
            Vector2Int c = new Vector2Int(cx, cy);
            if (!InBounds(c) || !water.Contains(c)) return 0f;
            return isWater ? 1f : Mathf.Max(1, depth[Idx(c)]);
        }
        float Sample(float x, float y, bool isWater)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float a = Mathf.Lerp(Cell(x0, y0, isWater), Cell(x0 + 1, y0, isWater), fx);
            float b = Mathf.Lerp(Cell(x0, y0 + 1, isWater), Cell(x0 + 1, y0 + 1, isWater), fx);
            return Mathf.Lerp(a, b, fy);
        }

        Color shallow = new Color(0.33f, 0.62f, 0.80f), deep = new Color(0.10f, 0.30f, 0.60f);
        Color sand = new Color(0.78f, 0.70f, 0.46f), soil = new Color(0.20f, 0.36f, 0.10f);
        // Рисуем только около реки (клетки воды и берега), остальное - трава
        var near = new HashSet<Vector2Int>();
        foreach (var w in water)
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    Vector2Int n = new Vector2Int(w.x + dx, w.y + dy);
                    if (InBounds(n)) near.Add(n);
                }
        foreach (var c in near)
        {
            int bx = (c.x + Half) * cellPx, by = (c.y + Half) * cellPx;
            for (int y = by; y < by + cellPx; y++)
                for (int x = bx; x < bx + cellPx; x++)
                {
                    float wx = (x + 0.5f) / cellPx - Half, wy = (y + 0.5f) / cellPx - Half;
                    float sw = Sample(wx - 0.5f, wy - 0.5f, true);
                    Color g = px[y * res + x];
                    Color o;
                    float a;
                    if (sw > 0.45f)
                    {
                        float dp = Sample(wx - 0.5f, wy - 0.5f, false);
                        o = Color.Lerp(shallow, deep, Mathf.Clamp01((dp - 1f) / 2f));
                        float ripple = Mathf.PerlinNoise(wx * 1.7f, wy * 0.6f + wx * 0.4f);
                        o = Color.Lerp(o, Color.white, Mathf.Max(0f, ripple - 0.72f) * 0.9f);
                        a = Mathf.Lerp(0.75f, 1f, Mathf.Clamp01((sw - 0.45f) / 0.2f));
                    }
                    else if (sw > 0.28f) { o = sand; a = Mathf.Clamp01((sw - 0.28f) / 0.1f) * 0.85f; }
                    else
                    {
                        Vector2Int cc = new Vector2Int(Mathf.FloorToInt(wx), Mathf.FloorToInt(wy));
                        int b = InBounds(cc) ? bank[Idx(cc)] : 0;
                        if (b <= 0 || b > 2) continue;
                        o = soil; a = b == 1 ? 0.30f : 0.16f;
                    }
                    Color m = Color.Lerp(g, o, a);
                    m.a = 1f;
                    px[y * res + x] = m;
                }
        }
    }

    // ---------- Клетки ----------

    public void MarkDirty() { dirty = true; }

    public bool InBounds(Vector2Int c) { return c.x >= -Half && c.x < Half && c.y >= -Half && c.y < Half; }
    int Idx(Vector2Int c) { return (c.x + Half) * size + (c.y + Half); }
    Vector2Int FromIdx(int i) { return new Vector2Int(i / size - Half, i % size - Half); }

    public Vector2Int WorldToCell(Vector2 w)
    {
        Vector2 l = FrameOf(w);
        return new Vector2Int(Mathf.FloorToInt(l.x), Mathf.FloorToInt(l.y));
    }

    public Vector2 CellCenter(Vector2Int c) { return Local(c.x + 0.5f, c.y + 0.5f); }

    /// <summary>Точка мира по локальной координате карты (не клетке), с учётом стороны высадки.</summary>
    public static Vector2 Local(float x, float y) { return Origin + WorldDir(new Vector2(x, y)); }
    public static Vector2 Local(Vector2 f) { return Origin + WorldDir(f); }

    void Rebuild()
    {
        int total = size * size;
        if (blocked == null || blocked.Length != total) blocked = new bool[total];
        else System.Array.Clear(blocked, 0, total);
        if (pushable == null || pushable.Length != total) pushable = new bool[total];
        else System.Array.Clear(pushable, 0, total);

        foreach (var b in RaidBuilding.All)
        {
            if (b == null || !b.Blocks) continue;
            for (int x = 0; x < b.SizeCells.x; x++)
                for (int y = 0; y < b.SizeCells.y; y++)
                {
                    Vector2Int c = new Vector2Int(b.OriginCell.x + x, b.OriginCell.y + y);
                    if (InBounds(c)) blocked[Idx(c)] = true;
                }
        }
        foreach (var t in trees) if (InBounds(t)) blocked[Idx(t)] = true;

        // Полевые укрепления на поле боя - сплошная стена для наших и для защитников.
        // Заслон на колёсах - не стена: его толкают (FieldPost.Pushable), путь через него дороже на PushPathCost
        foreach (var fp in FieldPost.All)
        {
            if (fp == null || !fp.Alive || !fp.Raid || fp.Cells == null) continue;
            bool push = fp.Pushable;
            foreach (var fc in fp.Cells)
                if (InBounds(fc)) { if (push) pushable[Idx(fc)] = true; else blocked[Idx(fc)] = true; }
        }
        dirty = false;
        failedPaths.Clear();   // карта изменилась - старые «пути нет» больше не верны
    }

    // Обновление 12: недавние безуспешные поиски (цель за сплошной стеной). Такой поиск раскрывает до MaxExpand клеток,
    // а бойцы просили его снова и снова - в большом бою это давало рывки. Пока карта не менялась - ответ тот же
    readonly Dictionary<long, float> failedPaths = new Dictionary<long, float>();
    const float FailedPathMemory = 1.5f;
    static long PathKey(Vector2Int a, Vector2Int b) { return ((long)((a.x + 512) * 1024 + a.y + 512) << 21) | (long)((b.x + 512) * 1024 + b.y + 512); }

    public bool IsBlocked(Vector2Int c)
    {
        if (!InBounds(c)) return true;
        if (dirty || blocked == null) Rebuild();
        return blocked[Idx(c)];
    }

    /// <summary>В клетке заслон на колёсах, который можно протолкнуть (для пути - проходима, но дорога).</summary>
    public bool IsPushable(Vector2Int c)
    {
        if (!InBounds(c)) return false;
        if (dirty || blocked == null) Rebuild();
        int i = Idx(c);
        return pushable[i] && !blocked[i];
    }

    public bool LineWalkable(Vector2 a, Vector2 b)
    {
        float len = Vector2.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.25f));
        for (int i = 1; i <= steps; i++)
        {
            Vector2Int c = WorldToCell(Vector2.Lerp(a, b, i / (float)steps));
            if (IsBlocked(c) || IsPushable(c)) return false;   // сквозь заслон на колёсах - только по A* (с его ценой)
        }
        return true;
    }

    public Vector2Int NearestFree(Vector2Int cell)
    {
        if (!IsBlocked(cell)) return cell;
        for (int r = 1; r <= 12; r++)
            for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                {
                    if (Mathf.Abs(x) != r && Mathf.Abs(y) != r) continue;
                    Vector2Int c = new Vector2Int(cell.x + x, cell.y + y);
                    if (!IsBlocked(c)) return c;
                }
        return cell;
    }

    public bool TryRandomWalkable(Vector2 center, float radius, out Vector2 point)
    {
        for (int i = 0; i < 20; i++)
        {
            Vector2 p = center + Random.insideUnitCircle * radius;
            Vector2Int c = WorldToCell(p);
            if (!IsBlocked(c)) { point = CellCenter(c) + Random.insideUnitCircle * 0.3f; return true; }
        }
        point = center;
        return false;
    }

    /// <summary>Свободная клетка у стены здания, ближайшая к from (куда подходить, чтобы его бить).</summary>
    public Vector2 ApproachPoint(RaidBuilding b, Vector2 from)
    {
        Vector2Int f0 = WorldToCell(from);
        Vector2Int o = b.OriginCell, sz = b.SizeCells;
        int best = int.MaxValue;
        Vector2Int pick = o;
        bool found = false;

        if (!b.Blocks)
        {
            // Проходимое (поле, руины стены) - можно встать прямо на него
            for (int x = 0; x < sz.x; x++)
                for (int y = 0; y < sz.y; y++)
                {
                    Vector2Int c = new Vector2Int(o.x + x, o.y + y);
                    int d = Mathf.Abs(c.x - f0.x) + Mathf.Abs(c.y - f0.y);
                    if (d < best) { best = d; pick = c; found = true; }
                }
            return found ? CellCenter(pick) : b.Center;
        }

        for (int x = -1; x <= sz.x; x++)
            for (int y = -1; y <= sz.y; y++)
            {
                bool ex = x == -1 || x == sz.x, ey = y == -1 || y == sz.y;
                if (ex == ey) continue;   // только боковые клетки периметра
                Vector2Int c = new Vector2Int(o.x + x, o.y + y);
                if (IsBlocked(c)) continue;
                int d = Mathf.Abs(c.x - f0.x) + Mathf.Abs(c.y - f0.y);
                if (d < best) { best = d; pick = c; found = true; }
            }
        return found ? CellCenter(pick) : b.Center;
    }

    static float Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
        return (dx + dy) + (1.414f - 2f) * Mathf.Min(dx, dy);
    }

    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
    {
        if (!InBounds(start) || !InBounds(goal) || IsBlocked(goal)) return null;
        if (start == goal) return new List<Vector2Int>();

        if (dirty || blocked == null) Rebuild();
        long key = PathKey(start, goal);
        if (failedPaths.TryGetValue(key, out float when) && Time.time - when < FailedPathMemory) return null;
        pathClock.Start();
        try
        {
            var path = Search(start, goal);
            if (path == null)
            {
                if (failedPaths.Count > 512) failedPaths.Clear();
                failedPaths[key] = Time.time;
            }
            return path;
        }
        finally { pathClock.Stop(); }
    }

    List<Vector2Int> Search(Vector2Int start, Vector2Int goal)
    {
        int total = size * size;
        if (g == null || g.Length != total)
        {
            g = new float[total]; f = new float[total]; parent = new int[total];
            seen = new int[total]; closedMark = new int[total];
            stamp = 0;
            heapIdx = new int[total * 4]; heapF = new float[total * 4];   // с запасом: узлы могут попадать в кучу повторно
        }
        if (++stamp == int.MaxValue)
        {
            System.Array.Clear(seen, 0, total);
            System.Array.Clear(closedMark, 0, total);
            stamp = 1;
        }
        int mark = stamp;

        int s = Idx(start), t = Idx(goal);
        g[s] = 0f;
        parent[s] = -1;
        seen[s] = mark;
        f[s] = Heuristic(start, goal);
        heapCount = 0;
        HeapPush(s, f[s]);
        int expanded = 0;

        while (heapCount > 0)
        {
            int cur = HeapPop();
            if (closedMark[cur] == mark) continue;
            closedMark[cur] = mark;
            if (++expanded > MaxExpand) return null;

            if (cur == t)
            {
                var result = new List<Vector2Int>();
                int c = t;
                while (c != s)
                {
                    result.Add(FromIdx(c));
                    c = parent[c];
                    if (c < 0) return null;
                }
                result.Reverse();
                return result;
            }

            Vector2Int cc = FromIdx(cur);
            for (int d = 0; d < Dirs.Length; d++)
            {
                Vector2Int dir = Dirs[d];
                Vector2Int nc = new Vector2Int(cc.x + dir.x, cc.y + dir.y);
                if (IsBlocked(nc)) continue;
                int ni = Idx(nc);
                if (closedMark[ni] == mark) continue;
                bool diag = dir.x != 0 && dir.y != 0;
                if (diag && (IsBlocked(new Vector2Int(cc.x + dir.x, cc.y)) || IsBlocked(new Vector2Int(cc.x, cc.y + dir.y)))) continue;
                float ng = g[cur] + (diag ? 1.414f : 1f) + (pushable[ni] ? FieldPost.PushPathCost : 0f);   // заслон на колёсах - протолкнуть
                if (seen[ni] != mark || ng < g[ni])
                {
                    seen[ni] = mark;
                    g[ni] = ng;
                    f[ni] = ng + Heuristic(nc, goal);
                    parent[ni] = cur;
                    if (heapCount < heapIdx.Length) HeapPush(ni, f[ni]);
                }
            }
        }
        return null;
    }

    // ---------- Двоичная куча для A* (карта большая - перебирать список целиком медленно) ----------

    int[] heapIdx;
    float[] heapF;
    int heapCount;

    void HeapPush(int idx, float pri)
    {
        int i = heapCount++;
        heapIdx[i] = idx;
        heapF[i] = pri;
        while (i > 0)
        {
            int p = (i - 1) / 2;
            if (heapF[p] <= heapF[i]) break;
            Swap(i, p);
            i = p;
        }
    }

    int HeapPop()
    {
        int top = heapIdx[0];
        heapCount--;
        if (heapCount > 0)
        {
            heapIdx[0] = heapIdx[heapCount];
            heapF[0] = heapF[heapCount];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < heapCount && heapF[l] < heapF[m]) m = l;
                if (r < heapCount && heapF[r] < heapF[m]) m = r;
                if (m == i) break;
                Swap(i, m);
                i = m;
            }
        }
        return top;
    }

    void Swap(int a, int b)
    {
        int ti = heapIdx[a]; heapIdx[a] = heapIdx[b]; heapIdx[b] = ti;
        float tf = heapF[a]; heapF[a] = heapF[b]; heapF[b] = tf;
    }
}

/// <summary>
/// Движение по карте набега (как GridMover для города): по прямой, если свободно, иначе по A*.
/// Fails - сколько раз подряд путь не нашёлся.
/// </summary>
public class RaidMover
{
    List<Vector2Int> path;
    int index;
    float repathTimer;
    Vector2Int pathGoal;

    // Проверка «по прямой свободно» - не каждый кадр (на далёкую цель это сотни клеток на бойца),
    // а раз в LineRecheck с, пока цель в той же клетке. Наткнулся на новую преграду - проверяем сразу.
    const float LineRecheck = 0.25f;
    bool lineOk, lineKnown;
    Vector2Int lineGoal;
    float lineUntil;

    public int Fails { get; private set; }
    float smoothAt;

    public void Clear() { path = null; repathTimer = 0f; lineKnown = false; }
    public void ResetFails() { Fails = 0; }

    public bool Step(Transform tr, RaidMap map, Vector2 target, float speed, float stopDist = 0.05f)
    {
        Vector2 pos = tr.position;
        if (Vector2.Distance(pos, target) <= stopDist) { path = null; return true; }

        Vector2Int cur = map.WorldToCell(pos);
        if (map.IsBlocked(cur))
        {
            cur = map.NearestFree(cur);
            tr.position = map.CellCenter(cur);
            pos = tr.position;
            path = null;
        }

        Vector2Int targetCell = map.WorldToCell(target);
        if (!lineKnown || targetCell != lineGoal || Time.time >= lineUntil)
        {
            lineOk = map.LineWalkable(pos, target);
            lineKnown = true;
            lineGoal = targetCell;
            lineUntil = Time.time + LineRecheck * Random.Range(0.8f, 1.2f);   // вразнобой, чтобы не все в одном кадре
        }

        if (lineOk)
        {
            Vector2 np = Vector2.MoveTowards(pos, target, speed * Time.deltaTime);
            if (map.IsBlocked(map.WorldToCell(np))) { lineKnown = false; return false; }   // впереди появилась преграда
            path = null;
            Fails = 0;
            tr.position = np;
            return Vector2.Distance(np, target) <= stopDist;
        }

        Vector2Int goal = map.WorldToCell(target);
        if (map.IsBlocked(goal)) goal = map.NearestFree(goal);

        repathTimer -= Time.deltaTime;
        bool valid = path != null && index < path.Count;
        bool wantNew = !valid || goal != pathGoal;

        if (wantNew && repathTimer <= 0f && goal != cur && RaidMap.TryUsePathBudget())
        {
            repathTimer = 0.6f;
            List<Vector2Int> p = map.FindPath(cur, goal);
            if (p != null && p.Count > 0)
            {
                path = p;
                index = 0;
                pathGoal = goal;
                Fails = 0;
                valid = true;
            }
            else
            {
                path = null;
                valid = false;
                Fails++;
            }
        }

        if (!valid) return false;

        // final-13: путь по клеткам идёт ломаной под 45° - бойцы «бегали странными прямыми линиями».
        // Раз в 0,2 с срезаем углы: если до одной из следующих точек пути можно пройти напрямую - идём сразу к ней
        if (Time.time >= smoothAt)
        {
            smoothAt = Time.time + 0.2f * Random.Range(0.8f, 1.2f);
            for (int k = Mathf.Min(path.Count - 1, index + 6); k > index; k--)
                if (map.LineWalkable(pos, map.CellCenter(path[k]))) { index = k; break; }
        }

        Vector2Int next = path[index];
        if (map.IsBlocked(next)) { path = null; repathTimer = 0f; return false; }
        Vector2 t = map.CellCenter(next);
        tr.position = Vector2.MoveTowards(pos, t, speed * Time.deltaTime);
        if (Vector2.Distance(tr.position, t) < 0.05f) index++;
        return false;
    }
}