using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Клановая война (klan-voina-2): живые деревни на общей квадратной карте.
///
/// На войну игрок переходит в свою военную деревню (WarCityMode): это копия домашней, и она живёт как дома -
/// жители добывают, мастерские и кузни работают, конюшни растят лошадей, казармы учат бойцов. Строить и улучшать
/// нельзя, ресурсы свои. Вокруг деревни лежит карта войны (Embedded): соседи по клану, враги, источники ресурсов.
/// Обученные бойцы сами выходят из казарм на карту и собираются в отряды по видам войск; отряды - кнопками внизу.
/// Своим можно дарить ресурсы и торговать на рынке клана («Клану»). Домой возвращается только рейтинг клана.
///
/// Без своей деревни на войне (или пока её не загрузили) карту можно открыть отдельно - только смотреть.
///
/// Управление (телефон): нажать на своего бойца - выбрать (дважды - всех такого вида на экране), кнопка отряда - весь отряд;
/// выбрали - нажать на землю - идти туда, на врага - напасть; вести пальцем - двигать карту, двумя пальцами - приблизить.
/// Компьютер: ЛКМ - выбрать (рамкой - многих), ЛКМ/ПКМ по земле - приказ (Ctrl - просто идти); S - стоп, H - стоять,
/// 1-6 - отряд, Ctrl+1-6 - в отряд, Ctrl+A - все, Пробел - к своей деревне, Esc - снять выбор.
/// </summary>
public partial class WarView : MonoBehaviour
{
    public static WarView Instance { get; private set; }
    /// <summary>Карта открыта отдельно (деревня спрятана, своя камера) - только смотреть. На войне карта лежит вокруг деревни, и это не IsOpen.</summary>
    public static bool IsOpen => Instance != null && Instance.open && !Instance.embedded;
    /// <summary>Карта войны вокруг живой военной деревни (WarCityMode).</summary>
    public static bool Embedded => Instance != null && Instance.open && Instance.embedded;

    static readonly Vector2 FarOrigin = new Vector2(3000f, -150f);   // отдельная карта: далеко от деревни
    static Vector2 Origin = FarOrigin;                                 // левый нижний угол карты в мире (на войне деревня - в начале координат)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; Origin = FarOrigin; }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    // ---------- Цвета ----------

    static readonly Color MyColor = new Color(0.30f, 0.55f, 0.95f);      // мои бойцы
    static readonly Color AllyColor = new Color(0.30f, 0.78f, 0.80f);    // бойцы моего клана
    static readonly Color EnemyColor = new Color(0.88f, 0.28f, 0.22f);
    static readonly Color MySideZone = new Color(0.25f, 0.5f, 1f);
    static readonly Color EnemyZone = new Color(1f, 0.25f, 0.2f);
    static readonly Color NeutralSrc = new Color(0.95f, 0.80f, 0.35f);
    const string IronHex = "#C3C8D4";

    // ---------- Состояние ----------

    bool open;
    bool embedded;      // карта вокруг военной деревни
    float embedRetry;
    WarStatic st;
    WarFrame fr;
    WarGeo G;
    byte[] terrain;
    int W, H;
    int eventN;
    bool staticLoading, frameLoading, focused, worldReady;
    float frameTimer, staticRetry, nextStaticAt, fogAt;
    int wantVersion = -1;
    string status = "";                 // почему нет карты (ошибка сервера)
    string building;                    // рисуем карту: сколько готово
    int fails;
    Coroutine buildJob;

    Camera cam;
    CameraController camCtl;
    Vector3 savedCamPos;
    float savedCamSize;

    GameObject root;
    Transform bldRoot, unitRoot;
    Texture2D ownerTex, fogTex, miniTex, miniOwnerTex;
    Color32[] ownerPix, fogPix, miniOwnerPix;
    bool[] vis;
    int vw, vh, visCell = 2;
    string lastPlots = "";
    int lastStage = -1;

    class BldView
    {
        public WarBldView d;
        public GameObject go;
        public SpriteRenderer sr, plate;
        public bool art, repairing, own;   // own - здание своей деревни: на карте не рисуем, оно настоящее
        public Color baseColor;
        public int hp = 100;
        public WarFire fire;               // klan-voina-14: огонь и дым на повреждённом здании
        public int side;
        public Vector2 Center => Origin + new Vector2(d.x + d.w / 2f, d.y + d.h / 2f);
        public bool Alive => hp > 0;
    }

    class UnitView
    {
        public int id, type, side, hp = 100, target = -1, squad, home = -1;
        public SpriteRenderer hpBack, hpFill;   // klan-voina-14: полоска здоровья раненого
        public bool mine, struck, moving, hold;
        public Vector2 pos, goal;          // в клетках карты
        public GameObject go;
        public ArtFigure fig;
        public Facing facing;
        public SpriteRenderer ring;
        public WarUnitDef Def => WarRules.Units[Mathf.Clamp(type, 0, WarRules.Units.Length - 1)];
        public Vector2 World => Origin + pos;
    }

    class SrcView
    {
        public WarSource d;
        public GameObject go;
        public SpriteRenderer sr, plate;
        public int owner = -1;
        public WarFlag flag;               // klan-voina-14: флаг стороны над захваченным источником
        public Vector2 Center => Origin + new Vector2(d.x + WarRules.SourceSize / 2f, d.y + WarRules.SourceSize / 2f);
    }

    readonly Dictionary<int, BldView> blds = new Dictionary<int, BldView>();
    readonly List<SrcView> srcs = new List<SrcView>();
    readonly Dictionary<int, UnitView> units = new Dictionary<int, UnitView>();
    readonly HashSet<int> selected = new HashSet<int>();
    int selectedBld = -1, selectedSrc = -1;

    // Куда мы послали своих бойцов (пунктир RouteMarks). Живёт только у нас: сервер и враг его не видят.
    struct Route { public Vector2 cell; public int unit, bld; public bool attack; }
    readonly Dictionary<int, Route> routes = new Dictionary<int, Route>();

    readonly Dictionary<string, float> seenShots = new Dictionary<string, float>();
    readonly List<string> plotCapture = new List<string>();   // "номер,сторона,%"

    struct Line { public string text; public float at; }
    readonly List<Line> feed = new List<Line>();
    string msg = "";
    float msgUntil;
    bool msgBad;

    // Интерфейс
    bool plainMove;         // приказ «просто идти» (без боя по пути)
    bool hireOpen, helpOpen;
    int hireTab;            // 0 найм, 1 улучшения, 2 ремонт
    int hireSquad;          // в какой отряд идут новые бойцы
    int panelMode;          // 0 приказы, 1 «в отряд», 2 новое имя отряда
    int renameSquad;
    string renameText = "";
    const string HelpKey = "war2.help", LiveHelpKey = "war3.help";
    bool chipOpen;          // на войне: плашка войны развёрнута (миникарта и события)
    bool giftOpen;          // окно «Клану»: подарки и рынок
    string giftTo;
    readonly int[] gift = new int[5];   // золото, еда, дерево, камень, железо

    // ---------- Открыть / закрыть ----------

    /// <summary>
    /// «На войну» (из окна клана): из дома - в свою военную деревню (вокруг неё вся карта). Своей деревни на войне нет - смотреть карту отдельно.
    /// Уже на войне - показать свою деревню.
    /// </summary>
    public static void Open()
    {
        if (Instance == null) return;
        if (Instance.open || WarCityMode.Active) { Instance.FocusMyCity(false); return; }
        if (MyCityInWar) WarCityMode.Enter();
        else Instance.DoOpen(false);
    }
    public static void Close() { if (Instance != null && !Instance.embedded) Instance.DoClose(); }

    /// <summary>У меня есть деревня на этой войне (схема клана), а карта уже есть (со второго дня).</summary>
    public static bool MyCityInWar
    {
        get
        {
            WarInfo w = ClanWindow.War;
            if (w == null || !w.inWar || w.slots == null) return false;
            WarStage s = (WarStage)w.stage;
            if (s < WarStage.Prepare || s >= WarStage.Finished) return false;
            string me = AccountManager.Profile != null ? AccountManager.Profile.id : null;
            return me != null && w.slots.Exists(x => x != null && x.accountId == me);
        }
    }

    void DoOpen(bool embed)
    {
        if (open) return;
        if (!AccountManager.LoggedIn || string.IsNullOrEmpty(AccountManager.Token)) return;
        if (RaidManager.ViewingBattle) { WarManager.Notify("Сначала вернитесь из набега к деревне (клавиша V)", false); return; }
        cam = Camera.main;
        if (cam == null) return;
        open = true;
        embedded = embed;
        camCtl = cam.GetComponent<CameraController>();
        if (!embed)
        {
            savedCamPos = cam.transform.position;
            savedCamSize = cam.orthographicSize;
            if (camCtl != null) camCtl.enabled = false;
        }
        st = null; fr = null; G = null; eventN = 0; status = ""; fails = 0; focused = false; worldReady = false;
        frameTimer = 0f; staticRetry = 0f; nextStaticAt = 0f; wantVersion = -1;
        feed.Clear();
        selected.Clear();
        selectedBld = selectedSrc = -1;
        hireOpen = false; panelMode = 0; giftOpen = false; chipOpen = PlayerPrefs.GetInt("war.chipOpen", 0) == 1;   // final-12-fix-1: миникарта по умолчанию свёрнута, выбор запоминается
        helpOpen = PlayerPrefs.GetInt(embed ? LiveHelpKey : HelpKey, 0) == 0;   // в первый раз - «Как играть»
        LoadStatic(false);
    }

    void DoClose()
    {
        if (!open) return;
        open = false;
        if (buildJob != null) { StopCoroutine(buildJob); buildJob = null; }
        ClearWorld();
        st = null; fr = null; G = null;
        if (embedded) CameraController.ClearViewArea();
        else if (cam != null)
        {
            cam.transform.position = savedCamPos;
            cam.orthographicSize = savedCamSize;
        }
        if (camCtl != null) camCtl.enabled = true;
        TouchControls.BoxSelect = false;
        embedded = false;
        giftOpen = false;
    }

    void ClearWorld()
    {
        if (root != null) Destroy(root);
        root = null;
        worldReady = false;
        blds.Clear();
        srcs.Clear();
        units.Clear();
        selected.Clear();
        routes.Clear();
        seenShots.Clear();
        selectedBld = selectedSrc = -1;
        lastPlots = "";
        lastStage = -1;
        momentsReady = false;   // klan-voina-14: объявления - заново с первого кадра
        moments.Clear();
        foreach (var t in new[] { ownerTex, fogTex, miniTex, miniOwnerTex, miniDots }) if (t != null) Destroy(t);
        ownerTex = fogTex = miniTex = miniOwnerTex = miniDots = null;
    }

    /// <summary>Кнопка «Домой» в военной деревне - левее кнопки «Клан», в ряд с ней.</summary>
    static Rect HomeButtonRect => new Rect(ClanWindow.CityButtonLeft - 12f - 128f, UiKit.TopBtnY, 128f, UiKit.TopBtnH);

    /// <summary>Нажатие - карте войны, а не деревне: карта открыта отдельно, или на войне - над окнами карты, своими бойцами, за краем деревни.</summary>
    public static bool IsPointerOver(Vector2 p)
    {
        if (Instance == null) return false;
        if (Instance.open && !Instance.embedded) return true;
        if (Instance.open && Instance.embedded && !coverCheck && Instance.TakesPointer(p)) return true;   // final-12-fix-1: свои плашки себя не закрывают
        return WarCityMode.Active && HomeButtonRect.Contains(p);
    }

    bool TakesPointer(Vector2 gp)
    {
        if (st == null || cam == null || !worldReady) return false;
        if (OverUI(gp)) return true;
        // Казарма ждёт, куда послать бойцов, - нажатие её (за краем деревни она сама выведет их на карту)
        BuildingInstance sel = BuildingPlacer.Selected;
        BarracksController br = sel != null ? sel.GetComponent<BarracksController>() : null;
        if (br != null && br.PickingTarget) return false;
        if (BuildingPlacer.CommandingSquad) return false;
        if (selected.Count > 0) return true;
        Vector2 w = MouseWorld(UiKit.ToScreen(gp));
        if (UnitAt(w, 0, true) != null) return true;
        return !InMyPlot(w);
    }

    /// <summary>Точка мира - в своей деревне (на войне она в начале координат).</summary>
    static bool InMyPlot(Vector2 world)
    {
        float h = WarRules.CitySize / 2f;
        return Mathf.Abs(world.x) < h && Mathf.Abs(world.y) < h;
    }

    /// <summary>Точка мира -> клетка карты войны.</summary>
    public static Vector2 WorldToCell(Vector2 world) { return world - Origin; }

    // ---------- Связь с сервером ----------

    void LoadStatic(bool skipTerrain)
    {
        if (staticLoading) return;
        staticLoading = true;
        nextStaticAt = Time.unscaledTime + 3f;   // не чаще раза в 3 секунды: на большой войне это сотни килобайт
        AccountManager.Backend.Clan(Api.WarStatic, AccountManager.Token, new ApiRequest { since = skipTerrain ? 1 : 0 }, res =>
        {
            staticLoading = false;
            if (!open) return;
            if (!res.ok || res.warStatic == null)
            {
                status = string.IsNullOrEmpty(res.error) ? "Карта войны недоступна" : res.error;
                staticRetry = 4f;
                return;
            }
            status = "";
            ApplyStatic(res.warStatic, skipTerrain);
        });
    }

    void RequestFrame()
    {
        frameLoading = true;
        AccountManager.Backend.Clan(Api.WarFrame, AccountManager.Token, new ApiRequest { since = eventN, live = embedded }, res =>
        {
            frameLoading = false;
            frameTimer = 0.25f;
            if (!open || st == null) return;
            if (!res.ok || res.warFrame == null)
            {
                fails++;
                frameTimer = 2f;
                if (fails >= 2) status = string.IsNullOrEmpty(res.error) ? "Нет связи с сервером" : res.error;
                return;
            }
            fails = 0;
            status = "";
            if (embedded && res.warFrame.gifts != null && res.warFrame.gifts.Count > 0) WarCityMode.TakeGifts(res.warFrame.gifts);
            ApplyFrame(res.warFrame);
        });
    }

    void Send(WarCommand cmd, System.Action ok = null) { Send(cmd, ok != null ? (System.Action<ApiResponse>)(_ => ok()) : null, null); }

    void Send(WarCommand cmd, System.Action<ApiResponse> ok, System.Action<ApiResponse> fail)
    {
        if (st == null || st.myCity < 0) return;
        NoteRoutes(cmd);
        AccountManager.Backend.Clan(Api.WarCmd, AccountManager.Token, new ApiRequest { warCmd = cmd }, res =>
        {
            if (!res.ok) { if (fail != null) fail(res); if (open) Say(res.error, true); }
            else if (ok != null) ok(res);
        });
    }

    // Приказ бойцам: запомнить, куда они идут (Стоп - маршрут снять)
    void NoteRoutes(WarCommand cmd)
    {
        if (cmd == null || cmd.units == null) return;
        bool move = cmd.kind == WarCommand.Move || cmd.kind == WarCommand.AttackMove;
        if (!move && cmd.kind != WarCommand.Attack && cmd.kind != WarCommand.Stop && cmd.kind != WarCommand.Hold) return;
        foreach (int id in cmd.units)
        {
            if (cmd.kind == WarCommand.Stop || cmd.kind == WarCommand.Hold) { routes.Remove(id); continue; }
            var r = new Route { cell = new Vector2(cmd.x, cmd.y), unit = -1, bld = -1 };
            if (cmd.kind == WarCommand.Attack)
            {
                r.attack = true;
                if (cmd.target >= 0) r.unit = cmd.target; else r.bld = -cmd.target - 1;
            }
            routes[id] = r;
        }
    }

    /// <summary>Пунктир от наших бойцов к цели приказа (зовёт RouteMarks каждый кадр). Дошёл, погиб, цель пропала - маршрут снимается.</summary>
    public void AddRoutes()
    {
        if (!open || routes.Count == 0) return;
        List<int> done = null;
        foreach (var kv in routes)
        {
            Route r = kv.Value;
            bool gone = !units.TryGetValue(kv.Key, out UnitView u) || !u.mine || u.hp <= 0;
            Vector2 to = Vector2.zero;
            float radius = 0.42f;
            if (!gone)
            {
                if (r.unit >= 0)
                {
                    if (units.TryGetValue(r.unit, out UnitView e) && e.hp > 0)
                    {
                        to = e.World;
                        gone = Vector2.Distance(e.pos, u.pos) <= u.Def.range + 0.8f;
                    }
                    else gone = true;
                }
                else if (r.bld >= 0)
                {
                    if (blds.TryGetValue(r.bld, out BldView b) && b.Alive)
                    {
                        to = b.Center;
                        radius = Mathf.Max(b.d.w, b.d.h) * 0.5f;
                        gone = Vector2.Distance(to, u.World) <= radius + u.Def.range + 0.8f;
                    }
                    else gone = true;
                }
                else
                {
                    to = Origin + r.cell;
                    gone = Vector2.Distance(r.cell, u.pos) < 1.2f;
                }
            }
            if (gone) { if (done == null) done = new List<int>(); done.Add(kv.Key); continue; }
            RouteMarks.Add(u.World, to, r.attack ? RouteMarks.AttackColor : RouteMarks.MoveColor, radius);
        }
        if (done != null) foreach (int id in done) routes.Remove(id);
    }

    void Say(string text, bool bad)
    {
        msg = text;
        msgBad = bad;
        msgUntil = Time.unscaledTime + 4f;
    }

    // ---------- Постоянная часть карты ----------

    void ApplyStatic(WarStatic s, bool skippedTerrain)
    {
        bool rebuild = st == null || root == null || st.warId != s.warId || st.seed != s.seed || st.rows != s.rows || st.gc != s.gc;
        if (rebuild && skippedTerrain)
        {
            // Другая война, а местность городов мы не спросили - спросим целиком
            staticLoading = false;
            LoadStatic(false);
            return;
        }
        st = s;
        if (rebuild)
        {
            if (buildJob != null) StopCoroutine(buildJob);
            buildJob = StartCoroutine(BuildWorld());
            return;
        }
        SyncBuildings();
        SyncSources();
    }

    IEnumerator BuildWorld()
    {
        ClearWorld();
        G = st.Geo;
        W = G.W;
        H = G.H;
        // На войне своя деревня стоит в начале координат - карта ложится вокруг неё
        WarCityView me = embedded ? MyCity : null;
        Origin = me != null ? -new Vector2(WarRules.CityCenterX(me.plotX), WarRules.CityCenterY(me.plotY)) : FarOrigin;
        building = "Рисуем карту войны…";
        yield return null;
        terrain = WarRules.Terrain(st.seed, G, st.cities);

        root = new GameObject("WarMap");
        bldRoot = new GameObject("Buildings").transform;
        bldRoot.SetParent(root.transform, false);
        unitRoot = new GameObject("Units").transform;
        unitRoot.SetParent(root.transform, false);

        yield return BuildTerrain();
        yield return BuildTrees();

        ownerTex = new Texture2D(G.Cols, G.Rows, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        ownerPix = new Color32[G.Cols * G.Rows];
        miniOwnerTex = new Texture2D(G.Cols, G.Rows, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        miniOwnerPix = new Color32[G.Cols * G.Rows];
        Quad("Owners", ownerTex, -90, 0.8f);

        // Туман: клетка тумана крупнее на большой карте (иначе миллион точек пересчитывать четыре раза в секунду)
        visCell = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(W, H) / 256f));
        vw = (W + visCell - 1) / visCell;
        vh = (H + visCell - 1) / visCell;
        vis = new bool[vw * vh];
        fogTex = new Texture2D(vw, vh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        fogPix = new Color32[vw * vh];
        for (int i = 0; i < fogPix.Length; i++) fogPix[i] = new Color32(8, 10, 16, 165);
        fogTex.SetPixels32(fogPix);
        fogTex.Apply();
        Quad("Fog", fogTex, 40, -0.5f);
        UpdateOwners();

        SyncBuildings();
        SyncSources();
        building = null;
        worldReady = true;
        buildJob = null;
        if (!focused) { focused = true; if (!embedded) FocusMyCity(true); }
    }

    GameObject Quad(string name, Texture2D tex, int order, float z)
    {
        GameObject q = PlainQuad.Create();
        q.name = name;
        q.transform.SetParent(root.transform, false);
        q.transform.position = new Vector3(Origin.x + W / 2f, Origin.y + H / 2f, z);
        q.transform.localScale = new Vector3(W, H, 1f);
        Renderer r = q.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        r.sortingOrder = order;
        return q;
    }

    Color TerrainColor(byte t, int y, float n)
    {
        switch (t)
        {
            case WarRules.Shallow: return Color.Lerp(new Color(0.30f, 0.58f, 0.78f), new Color(0.14f, 0.36f, 0.64f), 0.35f + n * 0.4f);
            case WarRules.Forest: return Color.Lerp(new Color(0.14f, 0.27f, 0.12f), new Color(0.19f, 0.33f, 0.15f), n);
            case WarRules.Grove: return Color.Lerp(new Color(0.20f, 0.36f, 0.16f), new Color(0.26f, 0.42f, 0.19f), n);
            case WarRules.Water: return Color.Lerp(new Color(0.16f, 0.33f, 0.58f), new Color(0.22f, 0.42f, 0.66f), n);
            case WarRules.Ford: return Color.Lerp(new Color(0.40f, 0.52f, 0.60f), new Color(0.55f, 0.58f, 0.52f), n);
            case WarRules.Field: return y % 2 == 0 ? new Color(0.74f, 0.64f, 0.30f) : new Color(0.64f, 0.55f, 0.25f);
            default:
                // На войне - та же трава, что в деревне: край деревни не виден
                if (embedded && BuildingPlacer.Instance != null) return Color.Lerp(BuildingPlacer.Instance.grassDark, BuildingPlacer.Instance.grassLight, n);
                return Color.Lerp(new Color(0.27f, 0.44f, 0.19f), new Color(0.38f, 0.56f, 0.26f), n);
        }
    }

    // Земля - одна картинка. Точек на клетку: 4 на маленькой карте, 2 на средней, 1 на большой (не больше 2048 точек в сторону)
    IEnumerator BuildTerrain()
    {
        int px = Mathf.Max(W, H) <= 512 ? 4 : Mathf.Max(W, H) <= 1024 ? 2 : 1;
        int tw = W * px, th = H * px;
        var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = px == 1 ? FilterMode.Bilinear : FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pix = new Color32[tw * th];
        // Миникарта - не больше 512 точек в сторону
        int ms = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(W, H) / 512f));
        int mw = (W + ms - 1) / ms, mh = (H + ms - 1) / ms;
        var mini = new Color32[mw * mh];
        var rng = new System.Random(st.seed);
        float ox = rng.Next(1000), oy = rng.Next(1000);
        Color border = new Color(0.92f, 0.80f, 0.42f);
        float lineK = px == 1 ? 0.12f : embedded ? 0.14f : 0.22f;
        float t0 = Time.realtimeSinceStartup;
        WarCityView me = embedded ? MyCity : null;

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                byte t = terrain[y * W + x];
                float n = Mathf.Clamp01(Mathf.PerlinNoise(ox + x * 0.08f, oy + y * 0.08f) * 0.75f + (float)rng.NextDouble() * 0.25f);
                Color c = TerrainColor(t, y, n);
                if (x % ms == 0 && y % ms == 0) mini[y / ms * mw + x / ms] = c;
                int zc = G.ZoneOfCell(x, y);
                bool zx = x > 0 && G.ZoneOfCell(x - 1, y) != zc, zy = y > 0 && G.ZoneOfCell(x, y - 1) != zc;
                bool lx = x % WarRules.Plot == 0, ly = y % WarRules.Plot == 0;
                // В своей деревне линий участков нет: там своя земля
                if (me != null && WarRules.PlotInCity(x / WarRules.Plot, y / WarRules.Plot, me.plotX, me.plotY)
                    && WarRules.PlotInCity((x - 1) / WarRules.Plot, (y - 1) / WarRules.Plot, me.plotX, me.plotY)) lx = ly = false;
                for (int iy = 0; iy < px; iy++)
                    for (int ix = 0; ix < px; ix++)
                    {
                        Color p = c;
                        if (px > 1)
                        {
                            double r = rng.NextDouble();
                            if (r < 0.08) p *= 1.08f;
                            else if (r < 0.16) p *= 0.92f;
                        }
                        // Границы участков - тонкие тёмные линии, границы земель кланов - золотые
                        if ((ix == 0 && lx) || (iy == 0 && ly)) p = Color.Lerp(p, Color.black, lineK);
                        if ((ix == 0 && zx) || (iy == 0 && zy)) p = Color.Lerp(p, border, 0.7f);
                        p.a = 1f;
                        pix[(y * px + iy) * tw + x * px + ix] = p;
                    }
            }
            if (Time.realtimeSinceStartup - t0 > 0.03f)
            {
                building = $"Рисуем карту войны… {y * 100 / H}%";
                yield return null;
                t0 = Time.realtimeSinceStartup;
            }
        }
        tex.SetPixels32(pix);
        tex.Apply(false, true);   // в памяти телефона копию не держим
        if (embedded) Quad("Ground", tex, -101, 1.5f);   // под землёй своей деревни
        else Quad("Ground", tex, -100, 1f);

        miniTex = new Texture2D(mw, mh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        miniTex.SetPixels32(mini);
        miniTex.Apply(false, true);
    }

    IEnumerator BuildTrees()
    {
        int forest = 0;
        foreach (byte t in terrain) if (t == WarRules.Forest || t == WarRules.Grove) forest++;
        if (forest == 0) yield break;
        float cap = UiKit.Phone ? 2500f : 6000f;   // деревьев на всю карту: лес и так виден по цвету земли
        float chance = Mathf.Min(0.8f, cap / forest);
        var rng = new System.Random(st.seed + 7);
        WarCityView me = embedded ? MyCity : null;
        Transform tr = new GameObject("Trees").transform;
        tr.SetParent(root.transform, false);
        float t0 = Time.realtimeSinceStartup;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                byte tt = terrain[y * W + x];
                if ((tt != WarRules.Forest && tt != WarRules.Grove) || rng.NextDouble() > (tt == WarRules.Grove ? chance * 0.5f : chance)) continue;
                if (me != null && WarRules.PlotInCity(x / WarRules.Plot, y / WarRules.Plot, me.plotX, me.plotY)) continue;   // свой лес рисует деревня
                var go = new GameObject("Tree");
                go.transform.SetParent(tr, false);
                float s = 0.75f + (float)rng.NextDouble() * 0.35f;
                go.transform.position = new Vector3(Origin.x + x + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.4f, Origin.y + y + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.4f, 0f);
                go.transform.localScale = new Vector3(s, s, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = -7;
                int variant = rng.NextDouble() < 0.35 ? 3 + rng.Next(2) : rng.Next(3);
                Sprite art = GameArt.Tree(variant);
                if (art != null) { sr.sprite = art; float v = 0.82f + (float)rng.NextDouble() * 0.18f; sr.color = new Color(v, v, v); }
                else { sr.sprite = CombatFx.Circle(); sr.color = new Color(0.16f, 0.36f, 0.14f); }
            }
            if (Time.realtimeSinceStartup - t0 > 0.03f) { yield return null; t0 = Time.realtimeSinceStartup; }
        }
    }

    // ---------- Здания ----------

    readonly Dictionary<string, Color> typeColors = new Dictionary<string, Color>();

    Color TypeColor(string type)
    {
        if (typeColors.TryGetValue(type, out Color c)) return c;
        c = new Color(0.6f, 0.6f, 0.62f);
        if (type == WarRules.HallName) c = new Color(0.85f, 0.72f, 0.40f);
        else if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.buildingTypes != null)
        {
            BuildingType bt = System.Array.Find(BuildingPlacer.Instance.buildingTypes, b => b != null && b.name == type);
            if (bt != null) c = bt.color;
        }
        typeColors[type] = c;
        return c;
    }

    WarCityView City(int idx) { return st != null && st.cities != null ? st.cities.Find(c => c.idx == idx) : null; }
    int SideOfCity(int idx) { WarCityView c = City(idx); return c != null ? c.side : 0; }
    WarCityView MyCity => st != null ? City(st.myCity) : null;

    void SyncBuildings()
    {
        if (bldRoot == null) return;
        var keep = new HashSet<int>();
        if (st.buildings != null)
            foreach (var d in st.buildings)
            {
                keep.Add(d.id);
                if (blds.TryGetValue(d.id, out BldView v))
                {
                    if (v.d.x != d.x || v.d.y != d.y) { v.d = d; PlaceBuilding(v); }
                    else v.d = d;
                    continue;
                }
                blds[d.id] = MakeBuilding(d);
            }
        var gone = new List<int>();
        foreach (var kv in blds) if (!keep.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (int id in gone) { Destroy(blds[id].go); blds.Remove(id); }
        if (selectedBld >= 0 && !blds.ContainsKey(selectedBld)) selectedBld = -1;
        JoinWalls();
    }

    // Стены и ворота соединяются с соседями (стены, ворота, башни), как дома: вертикальная стена - вертикальная
    static bool Joins(string type) { return type == WarRules.WallName || type == WarRules.GateName; }   // final-13-fix-3: без башен
    static long CellKey(int x, int y) { return ((long)x << 32) | (uint)y; }

    void JoinWalls()
    {
        var occ = new HashSet<long>();
        foreach (var v in blds.Values)
            if (Joins(v.d.type))
                for (int dx = 0; dx < v.d.w; dx++)
                    for (int dy = 0; dy < v.d.h; dy++) occ.Add(CellKey(v.d.x + dx, v.d.y + dy));
        foreach (var v in blds.Values)
        {
            if (!v.art || (v.d.type != WarRules.WallName && v.d.type != WarRules.GateName)) continue;
            int x = v.d.x, y = v.d.y, m = 0;
            for (int i = 0; i < v.d.w; i++) { if (occ.Contains(CellKey(x + i, y + v.d.h))) m |= GameArt.JoinN; if (occ.Contains(CellKey(x + i, y - 1))) m |= GameArt.JoinS; }
            for (int i = 0; i < v.d.h; i++) { if (occ.Contains(CellKey(x + v.d.w, y + i))) m |= GameArt.JoinE; if (occ.Contains(CellKey(x - 1, y + i))) m |= GameArt.JoinW; }
            int lv = Mathf.Max(1, v.d.level);
            Sprite sp = v.d.type == WarRules.GateName ? GameArt.GateLook(lv, GameArt.GateVertical(m), false) : GameArt.WallJoined(lv, m);
            if (sp == null || sp == v.sr.sprite) continue;
            v.sr.sprite = sp;
            PlaceBuilding(v);
        }
    }

    BldView MakeBuilding(WarBldView d)
    {
        var v = new BldView { d = d, side = SideOfCity(d.city) };
        v.go = new GameObject(d.type);
        v.go.transform.SetParent(bldRoot, false);

        // Подложка цвета клана: сразу видно, чьё здание
        var pl = new GameObject("Plate");
        pl.transform.SetParent(v.go.transform, false);
        v.plate = pl.AddComponent<SpriteRenderer>();
        v.plate.sprite = CombatFx.Square();
        Color zc = v.side == st.mySide ? MySideZone : EnemyZone;
        v.plate.color = new Color(zc.r, zc.g, zc.b, d.city == st.myCity ? 0.5f : 0.35f);
        v.plate.sortingOrder = -2;

        var look = new GameObject("Look");
        look.transform.SetParent(v.go.transform, false);
        v.sr = look.AddComponent<SpriteRenderer>();
        Sprite art = GameArt.Building(d.type, d.level);
        v.art = art != null;
        v.sr.sprite = art != null ? art : CombatFx.Square();
        v.baseColor = art != null ? Color.white : TypeColor(d.type);
        v.sr.color = v.baseColor;
        v.sr.sortingOrder = 0;
        if (art != null) ArtBuildingFx.Attach(v.sr);
        PlaceBuilding(v);
        v.own = embedded && d.city == st.myCity;
        if (v.own) { v.sr.enabled = false; v.plate.enabled = false; }   // свои здания - настоящие, в деревне
        return v;
    }

    void PlaceBuilding(BldView v)
    {
        WarBldView d = v.d;
        v.go.transform.position = new Vector3(Origin.x + d.x + d.w / 2f, Origin.y + d.y + d.h / 2f, 0f);
        Vector3 s = v.sr.sprite.bounds.size;
        v.sr.transform.localScale = new Vector3(d.w / Mathf.Max(0.01f, s.x), d.h / Mathf.Max(0.01f, s.y), 1f);
        v.sr.transform.localPosition = new Vector3(-v.sr.sprite.bounds.center.x * v.sr.transform.localScale.x, -v.sr.sprite.bounds.center.y * v.sr.transform.localScale.y, 0f);
        v.plate.transform.localScale = new Vector3(d.w + 0.25f, d.h + 0.25f, 1f);
    }

    void SetBuildingHp(BldView v, int hp, bool repairing)
    {
        v.repairing = repairing;
        if (v.hp == hp) return;
        bool wasAlive = v.Alive;
        v.hp = hp;
        BurnBuilding(v);   // klan-voina-14
        if (v.own)
        {
            // Своя деревня: враги снесли здание на карте - в деревне оно ломается сразу; починили - встаёт
            Vector2Int cell = OwnCell(v);
            if (wasAlive && !v.Alive) WarCityMode.RuinOne(cell);
            else if (!wasAlive && v.Alive) WarCityMode.RepairOne(cell);
            return;
        }
        if (wasAlive && !v.Alive)
        {
            Color c = v.baseColor;
            v.sr.color = new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 0.85f);
            v.plate.enabled = false;
            if (OnScreen(v.Center, 4f)) { CombatFx.Blast(v.Center, Mathf.Max(v.d.w, v.d.h) * 0.6f); CombatFx.Flash(v.Center); }
        }
        else if (!wasAlive && v.Alive)
        {
            // Отстроили
            v.sr.color = v.baseColor;
            v.plate.enabled = true;
        }
    }

    /// <summary>Клетка здания своей деревни в самой деревне (OriginCell).</summary>
    Vector2Int OwnCell(BldView v)
    {
        WarCityView me = MyCity;
        if (me == null) return new Vector2Int(-9999, -9999);
        return new Vector2Int(v.d.x - (int)WarRules.CityCenterX(me.plotX), v.d.y - (int)WarRules.CityCenterY(me.plotY));
    }

    BldView OwnBuildingAt(Vector2Int cell)
    {
        foreach (var v in blds.Values) if (v.own && OwnCell(v) == cell) return v;
        return null;
    }

    // ---------- Источники ----------

    void SyncSources()
    {
        if (root == null) return;
        int n = st.sources != null ? st.sources.Count : 0;
        if (srcs.Count == n) return;   // стоят на месте всю войну
        foreach (var v in srcs) if (v.go != null) Destroy(v.go);
        srcs.Clear();
        selectedSrc = -1;
        if (n == 0) return;
        var srcRoot = new GameObject("Sources").transform;
        srcRoot.SetParent(root.transform, false);
        foreach (var d in st.sources)
        {
            if (d == null) continue;
            var v = new SrcView { d = d };
            v.go = new GameObject(WarRules.SourceName(d.kind));
            v.go.transform.SetParent(srcRoot, false);
            v.go.transform.position = new Vector3(v.Center.x, v.Center.y, 0f);

            var pl = new GameObject("Plate");
            pl.transform.SetParent(v.go.transform, false);
            v.plate = pl.AddComponent<SpriteRenderer>();
            v.plate.sprite = CombatFx.Circle();
            v.plate.sortingOrder = -3;
            Vector3 cs = v.plate.sprite.bounds.size;
            float pd = WarRules.SourceSize + 1.6f;
            pl.transform.localScale = new Vector3(pd / Mathf.Max(0.01f, cs.x), pd / Mathf.Max(0.01f, cs.y), 1f);

            var look = new GameObject("Look");
            look.transform.SetParent(v.go.transform, false);
            v.sr = look.AddComponent<SpriteRenderer>();
            Sprite art = SourceArt(d.kind);
            v.sr.sprite = art != null ? art : CombatFx.Square();
            v.sr.color = art != null ? Color.white : SourceColor(d.kind);
            v.sr.sortingOrder = -1;
            Vector3 sz = v.sr.sprite.bounds.size;
            float k = WarRules.SourceSize / Mathf.Max(0.01f, Mathf.Max(sz.x, sz.y));
            look.transform.localScale = new Vector3(k, k, 1f);
            look.transform.localPosition = new Vector3(-v.sr.sprite.bounds.center.x * k, -v.sr.sprite.bounds.center.y * k, 0f);
            v.flag = WarFlag.On(v.go, new Vector2(WarRules.SourceSize * 0.32f, WarRules.SourceSize * 0.42f), 1.6f);   // klan-voina-14
            srcs.Add(v);
        }
        UpdateSourceOwners();
    }

    static Sprite SourceArt(int kind)
    {
        switch (kind)
        {
            case WarRules.SrcVillage: return GameArt.Building(BuildingInstance.FarmName, 2);
            case WarRules.SrcSawmill: return GameArt.Building(BuildingInstance.SawmillName, 2);
            case WarRules.SrcQuarry: return GameArt.Building(BuildingInstance.MineName, 1);
            default: return GameArt.Building(BuildingInstance.MineName, 3);
        }
    }

    static Color SourceColor(int kind)
    {
        switch (kind)
        {
            case WarRules.SrcVillage: return new Color(0.80f, 0.70f, 0.35f);
            case WarRules.SrcSawmill: return new Color(0.62f, 0.42f, 0.24f);
            case WarRules.SrcQuarry: return new Color(0.62f, 0.66f, 0.70f);
            default: return new Color(0.40f, 0.42f, 0.48f);
        }
    }

    int SourceOwner(WarSource d)
    {
        string plots = fr != null ? fr.plots : null;
        return plots != null && d.plot >= 0 && d.plot < plots.Length ? plots[d.plot] - '0' : 0;
    }

    Color OwnerColor(int owner) { return owner == 0 ? NeutralSrc : owner == st.mySide ? MySideZone : EnemyZone; }

    void UpdateSourceOwners()
    {
        foreach (var v in srcs)
        {
            int o = SourceOwner(v.d);
            if (o == v.owner) continue;
            v.owner = o;
            Color c = OwnerColor(o);
            v.plate.color = new Color(c.r, c.g, c.b, 0.45f);
            if (v.flag != null) v.flag.Set(o, st.mySide);
        }
    }

    int AliveCities(int side)
    {
        int n = 0;
        if (st.cities != null) foreach (var c in st.cities) if (c.side == side && !c.fallen) n++;
        return Mathf.Max(1, n);
    }

    SrcView SourceAt(Vector2 world)
    {
        float pick = Mathf.Max(WarRules.SourceSize * 0.7f, cam.orthographicSize * 0.03f);
        SrcView best = null;
        float bestD = pick;
        foreach (var v in srcs)
        {
            float d = Vector2.Distance(v.Center, world);
            if (d < bestD) { bestD = d; best = v; }
        }
        return best;
    }

    // ---------- Кадр ----------

    void ApplyFrame(WarFrame f)
    {
        bool over = f.stage >= (int)WarStage.Finished;
        // Открылись новые здания врага или перенесли / отстроили здание - перечитать постоянное (без местности городов)
        if (f.version != st.version && !over) wantVersion = f.version;
        if (f.events != null)
            foreach (string e in f.events) feed.Add(new Line { text = e, at = Time.unscaledTime });
        while (feed.Count > 30) feed.RemoveAt(0);
        eventN = f.eventN;

        if (!worldReady) { fr = f; return; }

        // Здоровье зданий: в кадре только повреждённые ("id,%,чинят"), остальные целые
        var hp = new Dictionary<int, int>();
        var rep = new HashSet<int>();
        foreach (string part in Split(f.blds))
        {
            string[] a = part.Split(',');
            if (a.Length < 2 || !int.TryParse(a[0], out int id) || !int.TryParse(a[1], out int pct)) continue;
            hp[id] = pct;
            if (a.Length >= 3 && a[2] == "1") rep.Add(id);
        }
        if (!over) foreach (var v in blds.Values) SetBuildingHp(v, hp.TryGetValue(v.d.id, out int p) ? p : 100, rep.Contains(v.d.id));

        if (!over)   // после конца бойцов не трогаем: пусть стоят, как стояли
        {
            ApplyUnits(f);
            ApplyShots(f);
        }

        Moments(f);   // klan-voina-14: объявления о главном (этап, участки, тревога, победа)
        plotCapture.Clear();
        plotCapture.AddRange(Split(f.capture));
        if (over && string.IsNullOrEmpty(f.plots) && fr != null) f.plots = fr.plots;   // после конца сервер участки не шлёт
        if (f.squadNames == null && fr != null) f.squadNames = fr.squadNames;
        if (f.mates == null && fr != null) f.mates = fr.mates;
        fr = f;
        if (f.plots != lastPlots || f.stage != lastStage) { UpdateOwners(); UpdateSourceOwners(); }
        if (Time.unscaledTime >= fogAt) { fogAt = Time.unscaledTime + 0.45f; UpdateFog(); }
    }

    static readonly string[] none = new string[0];
    static string[] Split(string s) { return string.IsNullOrEmpty(s) ? none : s.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries); }

    void ApplyUnits(WarFrame f)
    {
        var dead = new HashSet<int>();
        foreach (string d in Split(f.dead)) if (int.TryParse(d, out int id)) dead.Add(id);

        var present = new HashSet<int>();
        foreach (string part in Split(f.units))
        {
            string[] a = part.Split(',');
            if (a.Length < 8) continue;
            if (!int.TryParse(a[0], out int id)) continue;
            int.TryParse(a[1], out int type);
            int.TryParse(a[2], out int side);
            int.TryParse(a[3], out int x10);
            int.TryParse(a[4], out int y10);
            int.TryParse(a[5], out int hpPct);
            int.TryParse(a[6], out int flags);
            int.TryParse(a[7], out int target);
            int squad = 0, home = -1;
            if (a.Length >= 9) int.TryParse(a[8], out squad);
            if (a.Length >= 10 && !int.TryParse(a[9], out home)) home = -1;
            present.Add(id);
            Vector2 p = new Vector2(x10 / 10f, y10 / 10f);
            if (!units.TryGetValue(id, out UnitView u))
            {
                u = MakeUnit(id, type, side, (flags & 1) != 0, p);
                units[id] = u;
            }
            u.goal = p;
            u.home = home;
            if (u.hp != hpPct) SetUnitHp(u, hpPct);
            u.moving = (flags & 4) != 0;
            u.hold = (flags & 8) != 0;
            if (Time.unscaledTime >= squadLockUntil || !selected.Contains(id)) u.squad = squad;
            bool struck = (flags & 2) != 0;
            if (struck && !u.struck) Strike(u, target);
            u.struck = struck;
        }

        var gone = new List<int>();
        foreach (var kv in units) if (!present.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (int id in gone)
        {
            UnitView u = units[id];
            if (dead.Contains(id) && OnScreen(u.World, 2f)) CombatFx.Death(u.World);
            Destroy(u.go);
            units.Remove(id);
            selected.Remove(id);
        }
        if (embedded) ShareFieldSoldiers();
    }

    // klan-voina-14: свои бойцы с казармой - казармам деревни (WarFieldSoldiers): кто где и далеко ли от своей казармы
    void ShareFieldSoldiers()
    {
        var list = new List<WarFieldSoldiers.Field>();
        foreach (var u in units.Values)
        {
            if (!u.mine || u.home < 0 || !blds.TryGetValue(u.home, out BldView hb) || !hb.own || !hb.Alive) continue;
            float dx = Mathf.Max(0f, Mathf.Max(hb.d.x - u.goal.x, u.goal.x - (hb.d.x + hb.d.w)));
            float dy = Mathf.Max(0f, Mathf.Max(hb.d.y - u.goal.y, u.goal.y - (hb.d.y + hb.d.h)));
            WarUnitDef d = u.Def;
            list.Add(new WarFieldSoldiers.Field
            {
                id = u.id, unit = d.name, type = BarracksController.FindType(d.name), home = OwnCell(hb), hp = u.hp,
                world = Origin + u.goal, dist = Mathf.Sqrt(dx * dx + dy * dy), squad = u.squad,
            });
        }
        WarFieldSoldiers.Set(list);
    }

    /// <summary>klan-voina-14: послать своих бойцов к точке (мир) - «Позвать к казарме».</summary>
    public static bool OrderMoveTo(List<int> ids, Vector2 world)
    {
        WarView v = Instance;
        if (v == null || !v.open || v.st == null || v.st.myCity < 0 || ids == null || ids.Count == 0) return false;
        Vector2 cell = world - Origin;
        v.Send(new WarCommand { kind = WarCommand.Move, units = new List<int>(ids), x = cell.x, y = cell.y });
        if (v.OnScreen(world, 2f)) CombatFx.Ring(world, 0.8f, new Color(0.5f, 1f, 0.5f, 0.8f));
        return true;
    }

    /// <summary>klan-voina-14: показать своего бойца на карте и выбрать его («Найти» в казарме).</summary>
    public static void ShowUnit(int id, Vector2 world)
    {
        WarView v = Instance;
        if (v == null || !v.open) return;
        if (v.units.TryGetValue(id, out UnitView u) && u.mine)
        {
            v.selected.Clear();
            v.selected.Add(id);
            world = u.World;
        }
        if (CameraController.Instance != null) CameraController.Instance.FocusOn(world);
        CombatFx.Ring(world, 0.7f, new Color(0.45f, 1f, 0.45f, 0.9f));
    }

    /// <summary>Выбранные свои бойцы, что стоят у своей казармы (могут зайти в неё).</summary>
    List<int> SelectedNearHome(out BarracksController br)
    {
        br = null;
        var ids = new List<int>();
        foreach (int id in selected)
        {
            if (!WarFieldSoldiers.NearHome(id)) continue;
            ids.Add(id);
            if (br == null && units.TryGetValue(id, out UnitView u) && blds.TryGetValue(u.home, out BldView hb))
            {
                BuildingInstance b = OwnBuildingInVillage(OwnCell(hb));
                if (b != null) br = b.GetComponent<BarracksController>();
            }
        }
        return ids;
    }

    static BuildingInstance OwnBuildingInVillage(Vector2Int cell)
    {
        foreach (var b in BuildingInstance.All) if (b != null && b.OriginCell == cell) return b;
        return null;
    }

    UnitView MakeUnit(int id, int type, int side, bool mine, Vector2 p)
    {
        var u = new UnitView { id = id, type = type, side = side, mine = mine, pos = p, goal = p };
        WarUnitDef d = u.Def;
        u.go = new GameObject(d.name);
        u.go.transform.SetParent(unitRoot, false);
        u.go.transform.position = new Vector3(u.World.x, u.World.y, 0f);

        bool siege = d.name == "Полевая пушка" || d.name == "Катапульта";
        float scale = d.space >= 3 && !d.mounted && !siege ? 0.64f : 0.56f;
        Color body = side != st.mySide ? EnemyColor : mine ? MyColor : AllyColor;
        Color outline = mine ? new Color(0.98f, 0.95f, 0.75f) : Color.black;
        // klan-voina-14: свои бойцы выглядят так же, как в деревне: цвет и рост своего вида войск, без обводки набега
        UnitType home = mine ? BarracksController.FindType(d.name) : null;
        string kind = d.art;
        if (home != null)
        {
            body = home.color;
            if (home.bodyScale > 0.2f) scale = home.bodyScale;
            kind = GameArt.FigureKind(home) ?? d.art;
        }

        u.ring = Part(u.go, "Ring", Vector2.zero, new Vector2(0.95f, 0.95f), new Color(0.45f, 1f, 0.45f, 0.6f), 10);
        u.ring.sprite = CombatFx.Circle();
        u.ring.enabled = false;

        if (siege)
        {
            var rig = new GameObject("Rig");
            rig.transform.SetParent(u.go.transform, false);
            if (d.name == "Полевая пушка" && GameArt.FxCarriage(0) != null && GameArt.FxBarrel() != null)
            {
                SpriteRenderer car = Part(rig, "Carriage", new Vector2(0.14f, -0.1f), Vector2.one, Color.white, 15);
                car.sprite = GameArt.FxCarriage(0);
                float ck = 0.72f;
                car.transform.localScale = new Vector3(ck, ck, 1f);
                Vector2 tr = new Vector2(0.14f, -0.1f) + GameArt.CarriageTrunnion(0) * ck;
                SpriteRenderer barrel = Part(rig, "Barrel", tr + new Vector2(0.14f, 0.02f), Vector2.one, Color.white, 16);
                barrel.sprite = GameArt.FxBarrel();
                barrel.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
                barrel.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            }
            else if (d.name == "Полевая пушка")
            {
                Part(rig, "Wheels", new Vector2(0.12f, -0.2f), new Vector2(0.62f, 0.2f), new Color(0.35f, 0.24f, 0.12f), 15);
                Part(rig, "Carriage", new Vector2(-0.05f, -0.08f), new Vector2(0.5f, 0.12f), new Color(0.45f, 0.30f, 0.15f), 15);
                Part(rig, "Barrel", new Vector2(0.24f, 0.02f), new Vector2(0.62f, 0.17f), new Color(0.12f, 0.12f, 0.14f), 16);
            }
            else
            {
                Part(rig, "CatWheels", new Vector2(0.1f, -0.22f), new Vector2(0.66f, 0.13f), new Color(0.22f, 0.15f, 0.08f), 15);
                Part(rig, "CatFrame", new Vector2(0.1f, -0.1f), new Vector2(0.6f, 0.14f), new Color(0.45f, 0.3f, 0.15f), 15);
                var arm = Part(rig, "CatArm", new Vector2(0.18f, 0.08f), new Vector2(0.5f, 0.07f), new Color(0.66f, 0.5f, 0.28f), 16);
                arm.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            }
            float cs = 0.42f;
            SpriteRenderer col = home != null ? null : Part(rig, "Outline", new Vector2(-0.42f, 0f), new Vector2(cs + 0.1f, cs + 0.1f), outline, 12);
            SpriteRenderer cb = Part(rig, "Body", new Vector2(-0.42f, 0f), new Vector2(cs, cs), body, 13);
            u.fig = GameArt.Dress(cb, home != null ? GameArt.FigureKind(home) ?? d.art : d.art, col);
            u.facing = u.go.AddComponent<Facing>();
            u.facing.flipRoot = rig.transform;
            MakeHpBar(u, 0.5f);
            return u;
        }

        SpriteRenderer ol = home != null ? null : Part(u.go, "Outline", Vector2.zero, new Vector2(scale + 0.12f, scale + 0.12f), outline, 12);
        SpriteRenderer b = Part(u.go, "Body", Vector2.zero, new Vector2(scale, scale), body, 13);
        u.fig = GameArt.Dress(b, kind, ol);
        if (u.fig == null && kind != d.art) u.fig = GameArt.Dress(b, d.art, ol);
        if (home == null && !mine && side != st.mySide) MakeEnemyMark(u, scale);
        MakeHpBar(u, scale);
        if (d.mounted || (home != null && home.requiresHorse))
        {
            SpriteRenderer h = Part(u.go, "Horse", new Vector2(0f, -scale * 0.45f), new Vector2(scale * 1.5f, scale * 0.42f), new Color(0.45f, 0.29f, 0.14f), 11);
            if (u.fig != null) { GameArt.PlaceHorse(h, scale); u.fig.Mounted = true; u.fig.Horse = h; }
        }
        u.facing = u.go.AddComponent<Facing>();
        return u;
    }

    // klan-voina-14: под вражеским бойцом - красное пятно (свои в деревне его не носят): враг виден сразу
    void MakeEnemyMark(UnitView u, float scale)
    {
        SpriteRenderer m = Part(u.go, "EnemyMark", new Vector2(0f, -scale * 0.42f), new Vector2(scale * 1.15f, scale * 0.42f), new Color(1f, 0.18f, 0.12f, 0.45f), 9);
        m.sprite = CombatFx.Circle();
    }

    // Полоска здоровья над раненым бойцом (целым не видна)
    void MakeHpBar(UnitView u, float scale)
    {
        float y = Mathf.Max(0.42f, scale * 0.78f);
        u.hpBack = Part(u.go, "HpBack", new Vector2(0f, y), new Vector2(0.62f, 0.09f), new Color(0f, 0f, 0f, 0.7f), 18);
        u.hpFill = Part(u.go, "HpFill", new Vector2(0f, y), new Vector2(0.58f, 0.06f), Color.green, 19);
        u.hpBack.enabled = u.hpFill.enabled = false;
    }

    void SetUnitHp(UnitView u, int pct)
    {
        u.hp = pct;
        if (u.hpFill == null) return;
        bool show = pct < 100 && pct > 0;
        u.hpBack.enabled = u.hpFill.enabled = show;
        if (!show) return;
        float k = Mathf.Clamp01(pct / 100f);
        Transform t = u.hpFill.transform;
        Vector3 p = t.localPosition;
        t.localScale = new Vector3(0.58f * k, 0.06f, 1f);
        t.localPosition = new Vector3(-0.29f * (1f - k), p.y, 0f);
        u.hpFill.color = u.side != st.mySide ? Color.Lerp(new Color(0.75f, 0.1f, 0.08f), new Color(1f, 0.35f, 0.25f), k)
                                             : Color.Lerp(new Color(0.95f, 0.25f, 0.15f), new Color(0.35f, 0.9f, 0.3f), k);
    }

    static SpriteRenderer Part(GameObject parent, string n, Vector2 pos, Vector2 size, Color c, int order)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = CombatFx.Square();
        r.color = c;
        r.sortingOrder = order;
        return r;
    }

    /// <summary>Боец ударил или выстрелил: выпад и снаряд к цели.</summary>
    void Strike(UnitView u, int target)
    {
        Vector2 a = u.World;
        Vector2 b;
        if (target >= 0 && units.TryGetValue(target, out UnitView t)) b = t.World;
        else if (target < -1 && blds.TryGetValue(-target - 1, out BldView bv)) b = bv.Center;
        else return;
        if (!OnScreen(a, 6f) && !OnScreen(b, 6f)) return;   // далеко от камеры - ни выпада, ни снаряда
        if (u.facing != null) u.facing.LookAt(b);
        if (u.fig != null) u.fig.Lunge();
        WarUnitDef d = u.Def;
        if (d.range <= 2f) return;
        FieldPost.VisualShots = true;   // выстрел на карте войны - только картинка: заслоны в деревне не двигает
        if (d.name == "Полевая пушка") CombatFx.Slug(a, b, d.aoe, null);
        else if (d.name == "Катапульта") CombatFx.Shell(a, b, 0.9f, d.aoe, null);
        else if (d.name == "Поджигатель") CombatFx.Bucket(a, b, d.aoe, null);
        else if (d.art == "mage") { if (d.aoe > 0f) CombatFx.Fireball(a, b, d.aoe, null); else CombatFx.Bolt(a, b); }
        else CombatFx.Arrow(a, b);
        FieldPost.VisualShots = false;
    }

    void ApplyShots(WarFrame f)
    {
        float now = Time.unscaledTime;
        foreach (string s in Split(f.shots))
        {
            if (seenShots.ContainsKey(s)) continue;
            seenShots[s] = now;
            string[] a = s.Split(',');
            if (a.Length < 3 || !int.TryParse(a[0], out int id) || !blds.TryGetValue(id, out BldView v)) continue;
            int.TryParse(a[1], out int x10);
            int.TryParse(a[2], out int y10);
            Vector2 to = Origin + new Vector2(x10 / 10f, y10 / 10f);
            Vector2 from = v.Center + new Vector2(0f, v.d.h * 0.3f);
            if (!OnScreen(from, 6f) && !OnScreen(to, 6f)) continue;
            FieldPost.VisualShots = true;
            if (v.d.type == WarRules.TowerName || v.d.type == WarRules.HallName) CombatFx.Arrow(from, to);
            else CombatFx.Shell(from, to, 0.55f, 1.4f, null);
            FieldPost.VisualShots = false;
        }
        if (seenShots.Count > 200)
        {
            var old = new List<string>();
            foreach (var kv in seenShots) if (now - kv.Value > 5f) old.Add(kv.Key);
            foreach (string k in old) seenShots.Remove(k);
        }
    }

    // ---------- Участки и туман ----------

    WarStage Stage => fr != null ? (WarStage)fr.stage : ClanWindow.War != null ? (WarStage)ClanWindow.War.stage : WarStage.Prepare;

    void UpdateOwners()
    {
        if (ownerTex == null) return;
        string plots = fr != null ? fr.plots : null;
        WarStage stage = Stage;
        for (int py = 0; py < G.Rows; py++)
            for (int px = 0; px < G.Cols; px++)
            {
                int i = py * G.Cols + px;
                int home = G.ZoneOfPlot(px, py);
                int owner = plots != null && i < plots.Length ? plots[i] - '0' : home;
                Color c = new Color(0f, 0f, 0f, 0f), m = c;
                // final-12-fix-1: на войне свои земли без заливки, как и своя деревня - иначе вокруг деревни виден квадрат
                if (owner == st.mySide) { c = new Color(MySideZone.r, MySideZone.g, MySideZone.b, home == owner ? (embedded ? 0f : 0.06f) : 0.14f); m = new Color(MySideZone.r, MySideZone.g, MySideZone.b, 0.38f); }
                else if (owner != 0) { c = new Color(EnemyZone.r, EnemyZone.g, EnemyZone.b, home == owner ? 0.06f : 0.14f); m = new Color(EnemyZone.r, EnemyZone.g, EnemyZone.b, 0.38f); }
                // Куда пока нельзя - темнее
                if (!WarRules.CanEnter(st.mySide, home, stage)) { c = Color.Lerp(c, new Color(0f, 0f, 0f, 0.45f), embedded ? 0.35f : 0.7f); m = Color.Lerp(m, new Color(0f, 0f, 0f, 0.5f), 0.6f); }
                if (embedded && MyCity != null && WarRules.PlotInCity(px, py, MyCity.plotX, MyCity.plotY)) c = new Color(0f, 0f, 0f, 0f);   // своя деревня - без заливки
                ownerPix[i] = c;
                miniOwnerPix[i] = m;
            }
        ownerTex.SetPixels32(ownerPix);
        ownerTex.Apply();
        miniOwnerTex.SetPixels32(miniOwnerPix);
        miniOwnerTex.Apply();
        lastPlots = plots ?? "";
        lastStage = fr != null ? fr.stage : -1;
    }

    void UpdateFog()
    {
        if (fogTex == null) return;
        System.Array.Clear(vis, 0, vis.Length);
        for (int py = 0; py < G.Rows; py++)
            for (int px = 0; px < G.Cols; px++)
                if (G.ZoneOfPlot(px, py) == st.mySide) MarkRect(px * WarRules.Plot, py * WarRules.Plot, WarRules.Plot, WarRules.Plot);
        foreach (var u in units.Values)
            if (u.side == st.mySide) MarkCircle(u.goal.x, u.goal.y, u.Def.vision > 0 ? u.Def.vision : WarRules.UnitVision);
        foreach (var b in blds.Values)
        {
            if (b.side != st.mySide || !b.Alive) continue;
            bool shooter = WarRules.Shoots(b.d.type, b.d.level, out _, out _, out _, out _);
            if (!shooter) continue;   // обзор дают башни, пушки и Цитадель (как на сервере)
            MarkCircle(b.d.x + b.d.w / 2f, b.d.y + b.d.h / 2f, b.d.type == WarRules.TowerName ? WarRules.TowerVision : WarRules.BuildingVision + 4f);
        }
        var dark = new Color32(8, 10, 16, (byte)(embedded ? 110 : 150));   // на войне земля под туманом видна, бойцы врага - нет
        var clear = new Color32(8, 10, 16, 0);
        for (int i = 0; i < vis.Length; i++) fogPix[i] = vis[i] ? clear : dark;
        fogTex.SetPixels32(fogPix);
        fogTex.Apply();
    }

    void MarkRect(int x, int y, int w, int h)
    {
        for (int yy = y / visCell; yy <= (y + h - 1) / visCell && yy < vh; yy++)
            for (int xx = x / visCell; xx <= (x + w - 1) / visCell && xx < vw; xx++)
                vis[yy * vw + xx] = true;
    }

    void MarkCircle(float cx, float cy, float r)
    {
        int x0 = Mathf.Max(0, (int)((cx - r) / visCell)), x1 = Mathf.Min(vw - 1, (int)((cx + r) / visCell));
        int y0 = Mathf.Max(0, (int)((cy - r) / visCell)), y1 = Mathf.Min(vh - 1, (int)((cy + r) / visCell));
        float r2 = (r + visCell * 0.5f) * (r + visCell * 0.5f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f) * visCell - cx, dy = (y + 0.5f) * visCell - cy;
                if (dx * dx + dy * dy <= r2) vis[y * vw + x] = true;
            }
    }

    // ---------- Каждый кадр ----------

    void Update()
    {
        // На войне карта всегда вокруг деревни
        if (!open && WarCityMode.Active && AccountManager.LoggedIn && !AccountManager.Blocking &&
            BuildingPlacer.Instance != null && BuildingPlacer.Instance.WorldReady && Time.unscaledTime >= embedRetry)
        {
            embedRetry = Time.unscaledTime + 3f;
            DoOpen(true);
        }
        if (!open) return;
        if (!AccountManager.LoggedIn || (embedded && !WarCityMode.Active)) { DoClose(); return; }
        if (embedded && st != null && G != null) CameraController.SetViewArea(Origin + new Vector2(W / 2f, H / 2f), Mathf.Max(W, H));
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        float dt = Time.unscaledDeltaTime;
        if (st == null)
        {
            staticRetry -= dt;
            if (staticRetry <= 0f && !staticLoading) LoadStatic(false);
        }
        else
        {
            frameTimer -= dt;
            if (frameTimer <= 0f && !frameLoading) RequestFrame();
            if (wantVersion >= 0 && wantVersion != st.version && !staticLoading && Time.unscaledTime >= nextStaticAt && worldReady) { wantVersion = -1; LoadStatic(true); }
            if (worldReady) MoveUnits(Time.deltaTime);
        }
        if (embedded) HandleEmbeddedInput();
        else HandleInput(dt);
    }

    void MoveUnits(float dt)
    {
        foreach (var u in units.Values)
        {
            float d = Vector2.Distance(u.pos, u.goal);
            if (d > 6f) u.pos = u.goal;
            else if (d > 0.001f) u.pos = Vector2.MoveTowards(u.pos, u.goal, Mathf.Max(u.Def.speed * 1.2f, d * 3f) * dt);
            u.go.transform.position = new Vector3(u.World.x, u.World.y, 0f);
            bool sel = selected.Contains(u.id);
            if (u.ring.enabled != sel) u.ring.enabled = sel;
        }
    }

    // ---------- Управление ----------

    bool leftDown, rightDown, rightPan, midDown, miniDrag;
    Vector2 leftStart, rightStart;
    Vector3 panWorld;
    float lastClickAt, squadLockUntil;
    int lastClickUnit = -1;

    // Где на экране окна (с прошлого кадра): нажатие туда - окну, не карте
    readonly List<Rect> uiRects = new List<Rect>(), uiRectsNext = new List<Rect>();
    void Ui(Rect r) { uiRectsNext.Add(r); }

    Vector2 MouseWorld(Vector2 screen)
    {
        Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
        return new Vector2(w.x, w.y);
    }

    Vector2 ToGui(Vector2 world)
    {
        Vector3 s = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
        return UiKit.ToGui(s);
    }

    bool OnScreen(Vector2 world, float margin)
    {
        if (cam == null) return false;
        float h = cam.orthographicSize + margin, w = cam.orthographicSize * cam.aspect + margin;
        Vector3 c = cam.transform.position;
        return Mathf.Abs(world.x - c.x) <= w && Mathf.Abs(world.y - c.y) <= h;
    }

    bool OverUI(Vector2 gp)
    {
        if (ClanWindow.IsPointerOver(gp)) return true;   // окно клана поверх карты
        foreach (var r in uiRects) if (r.Contains(gp)) return true;
        return false;
    }

    bool AnyPanelOpen => helpOpen || hireOpen || panelMode != 0;

    void HandleInput(float dt)
    {
        Mouse m = Mouse.current;
        Keyboard kb = Keyboard.current;
        if (m == null) return;
        Vector2 sp = m.position.ReadValue();
        Vector2 gp = UiKit.ToGui(sp);
        bool overUI = OverUI(gp);
        bool ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
        bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

        // Клавиши
        if (kb != null && !UiKit.Typing && !MobileInput.Busy)
        {
            if (kb.escapeKey.wasPressedThisFrame && !ClanWindow.EatsEscape)
            {
                if (helpOpen) CloseHelp();
                else if (panelMode != 0) panelMode = 0;
                else if (selected.Count > 0 || selectedBld >= 0 || selectedSrc >= 0) ClearSelection();
                else if (hireOpen) hireOpen = false;
                else { DoClose(); return; }
            }
            if (st != null && worldReady)
            {
                if (kb.sKey.wasPressedThisFrame && !ctrl && selected.Count > 0) Send(new WarCommand { kind = WarCommand.Stop, units = new List<int>(selected) });
                if (kb.hKey.wasPressedThisFrame && !ctrl && selected.Count > 0) Send(new WarCommand { kind = WarCommand.Hold, units = new List<int>(selected) });
                if (ctrl && kb.aKey.wasPressedThisFrame) SelectSquad(-1);
                if (kb.spaceKey.wasPressedThisFrame) FocusMyCity(false);
                KeyControl[] digits = { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key };
                for (int i = 0; i < digits.Length; i++)
                    if (digits[i].wasPressedThisFrame) { if (ctrl) AssignSquad(i + 1); else SelectSquad(i + 1); }
                if (kb.digit0Key.wasPressedThisFrame) { if (ctrl) AssignSquad(0); else SelectSquad(0); }
            }
            Vector2 pan = Vector2.zero;
            if (kb.leftArrowKey.isPressed) pan.x -= 1f;
            if (kb.rightArrowKey.isPressed) pan.x += 1f;
            if (kb.upArrowKey.isPressed) pan.y += 1f;
            if (kb.downArrowKey.isPressed) pan.y -= 1f;
            if (pan != Vector2.zero) cam.transform.position += (Vector3)(pan * cam.orthographicSize * 1.4f * dt);
        }
        if (st == null || !worldReady) return;

        // Колесо - приблизить к курсору
        float scroll = m.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f && !overUI)
        {
            Vector2 before = MouseWorld(sp);
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * (scroll > 0 ? 0.88f : 1.14f), 4f, MaxZoom);
            Vector2 after = MouseWorld(sp);
            cam.transform.position += (Vector3)(before - after);
        }

        // Два пальца на телефоне - плавное приближение к точке между пальцами
        float pinch = TouchInput.PinchFactor;
        if (pinch != 1f)
        {
            Vector2 pc = TouchInput.PinchCenter;
            Vector2 before = MouseWorld(pc);
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize / pinch, 4f, MaxZoom);
            Vector2 after = MouseWorld(pc);
            cam.transform.position += (Vector3)(before - after);
        }

        // Средняя кнопка (на телефоне - один палец) - двигать карту
        if (m.middleButton.wasPressedThisFrame && !overUI) { midDown = true; panWorld = MouseWorld(sp); }
        if (midDown)
        {
            if (!m.middleButton.isPressed) midDown = false;
            else cam.transform.position += (Vector3)((Vector2)panWorld - MouseWorld(sp));
        }

        // Правая (на телефоне - долгое нажатие): тянуть - двигать карту, щелчок - приказ выбранным
        Rect mini = MiniRect;
        if (m.rightButton.wasPressedThisFrame && mini.Contains(gp)) MiniOrder(gp, UiKit.Phone || ctrl);
        else if (m.rightButton.wasPressedThisFrame && !overUI) { rightDown = true; rightPan = false; rightStart = sp; panWorld = MouseWorld(sp); }
        if (rightDown)
        {
            if (!rightPan && (sp - rightStart).sqrMagnitude > 64f) rightPan = true;
            if (rightPan && m.rightButton.isPressed) cam.transform.position += (Vector3)((Vector2)panWorld - MouseWorld(sp));
            if (!m.rightButton.isPressed)
            {
                rightDown = false;
                // На телефоне долгое нажатие - «просто идти» (без боя по пути)
                if (!rightPan) Order(MouseWorld(sp), UiKit.Phone ? true : ctrl || plainMove);
            }
        }

        // Левая: по миникарте - перейти (вести - смотреть), щелчок - выбрать или приказ, рамка - выбрать многих
        if (m.leftButton.wasPressedThisFrame && mini.Contains(gp)) miniDrag = true;
        if (miniDrag)
        {
            if (!m.leftButton.isPressed) miniDrag = false;
            else MiniJump(gp);
        }
        else
        {
            if (m.leftButton.wasPressedThisFrame && !overUI) { leftDown = true; leftStart = sp; }
            if (leftDown && !m.leftButton.isPressed)
            {
                leftDown = false;
                if ((sp - leftStart).sqrMagnitude > 64f) { if (!UiKit.Phone || TouchControls.BoxSelect) BoxSelect(leftStart, sp, shift); TouchControls.BoxSelect = false; }
                else Click(MouseWorld(sp), shift, ctrl);
            }
        }

        ClampCamera();
    }

    bool leftTake;

    // На войне камерой ведает деревня (CameraController: палец или ПКМ тянуть, колесо, два пальца), здесь - только бойцы карты
    void HandleEmbeddedInput()
    {
        Mouse m = Mouse.current;
        Keyboard kb = Keyboard.current;
        if (m == null || st == null || !worldReady) return;
        Vector2 sp = m.position.ReadValue();
        Vector2 gp = UiKit.ToGui(sp);
        bool overUI = OverUI(gp);
        bool cityUI = BuildingPlacer.PointerOverUI;
        if (overUI) CameraController.BlockZoom();
        bool ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
        bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

        if (kb != null && !UiKit.Typing && !MobileInput.Busy)
        {
            if (kb.escapeKey.wasPressedThisFrame && !ClanWindow.EatsEscape)
            {
                if (helpOpen) CloseHelp();
                else if (giftOpen) giftOpen = false;
                else if (panelMode != 0) panelMode = 0;
                else if (selected.Count > 0 || selectedBld >= 0 || selectedSrc >= 0) ClearSelection();
            }
            if (kb.sKey.wasPressedThisFrame && !ctrl && selected.Count > 0) Send(new WarCommand { kind = WarCommand.Stop, units = new List<int>(selected) });
            if (kb.hKey.wasPressedThisFrame && !ctrl && selected.Count > 0) Send(new WarCommand { kind = WarCommand.Hold, units = new List<int>(selected) });
            if (ctrl && kb.aKey.wasPressedThisFrame) SelectSquad(-1);
            if (kb.spaceKey.wasPressedThisFrame && !WeaponForge.IsPointerOver(gp)) FocusMyCity(false);
            if (selected.Count > 0 || ctrl)
            {
                // Цифры без выбора - деревне (там свои клавиши); с выбором или с Ctrl - отрядам
                KeyControl[] digits = { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key };
                for (int i = 0; i < digits.Length; i++)
                    if (digits[i].wasPressedThisFrame) { if (ctrl) AssignSquad(i + 1); else SelectSquad(i + 1); }
            }
        }

        // ПКМ (на телефоне - долгое нажатие): щелчок - приказ выбранным (тянуть - двигать карту, это делает камера)
        Rect mini = MiniRect;
        if (m.rightButton.wasPressedThisFrame && mini.Contains(gp)) MiniOrder(gp, UiKit.Phone || ctrl);
        else if (m.rightButton.wasPressedThisFrame && !overUI && !cityUI && selected.Count > 0) { rightDown = true; rightStart = sp; }
        if (rightDown && !m.rightButton.isPressed)
        {
            rightDown = false;
            if ((sp - rightStart).sqrMagnitude <= 64f) Order(MouseWorld(sp), UiKit.Phone || ctrl || plainMove);
        }

        // ЛКМ: по миникарте - перейти; щелчок - выбрать бойца или приказ; рамка - выбрать многих
        if (m.leftButton.wasPressedThisFrame && mini.Contains(gp)) miniDrag = true;
        if (miniDrag)
        {
            if (!m.leftButton.isPressed) miniDrag = false;
            else MiniJump(gp);
            return;
        }
        if (m.leftButton.wasPressedThisFrame && !overUI && !cityUI) { leftDown = true; leftStart = sp; leftTake = TakesPointer(gp); }
        if (leftDown && !m.leftButton.isPressed)
        {
            leftDown = false;
            // final-13-fix-2: на телефоне палец двигает карту, как в деревне - рамки выделения нет (отряды - кнопками внизу)
            if ((sp - leftStart).sqrMagnitude > 64f) { if (!UiKit.Phone && (leftTake || AnyMineIn(leftStart, sp))) BoxSelect(leftStart, sp, shift); }
            else if (leftTake) Click(MouseWorld(sp), shift, ctrl);
        }
    }

    bool AnyMineIn(Vector2 a, Vector2 b)
    {
        Vector2 wa = MouseWorld(a), wb = MouseWorld(b);
        Rect r = Rect.MinMaxRect(Mathf.Min(wa.x, wb.x), Mathf.Min(wa.y, wb.y), Mathf.Max(wa.x, wb.x), Mathf.Max(wa.y, wb.y));
        foreach (var u in units.Values) if (u.mine && r.Contains(u.World)) return true;
        return false;
    }

    /// <summary>Камера деревни: вся карта войны или снова деревня.</summary>
    bool ShowsWholeMap => cam != null && cam.orthographicSize > 60f;

    void ToggleWholeMap()
    {
        CameraController cc = CameraController.Instance;
        if (cc == null || cam == null) return;
        if (ShowsWholeMap) { cc.FocusOn(Vector2.zero); cc.SetZoomNow(18f); return; }
        cc.FocusOn(Origin + new Vector2(W / 2f, H / 2f));
        cc.SetZoomNow(Mathf.Max(H / 2f + 6f, (W / 2f + 6f) / Mathf.Max(0.5f, cam.aspect)));
    }

    float MaxZoom => Mathf.Max(20f, Mathf.Max(H / 2f + 30f, (W / 2f + 30f) / Mathf.Max(0.5f, cam.aspect)));

    void ClampCamera()
    {
        Vector3 p = cam.transform.position;
        p.x = Mathf.Clamp(p.x, Origin.x - 4f, Origin.x + W + 4f);
        p.y = Mathf.Clamp(p.y, Origin.y - 4f, Origin.y + H + 4f);
        p.z = -10f;
        cam.transform.position = p;
    }

    void FocusMyCity(bool zoom)
    {
        if (embedded || WarCityMode.Active) { if (CameraController.Instance != null) CameraController.Instance.FocusOn(Vector2.zero); return; }
        if (st == null || cam == null || G == null) return;
        Vector2 c = new Vector2(W / 2f, H / 2f);
        WarCityView me = MyCity;
        if (me != null) c = new Vector2(WarRules.CityCenterX(me.plotX), WarRules.CityCenterY(me.plotY));
        cam.transform.position = new Vector3(Origin.x + c.x, Origin.y + c.y, -10f);
        if (zoom) cam.orthographicSize = me != null ? 26f : Mathf.Min(MaxZoom, H / 2f + 6f);
    }

    void FocusOn(IEnumerable<int> ids)
    {
        Vector2 sum = Vector2.zero;
        int n = 0;
        foreach (int id in ids) if (units.TryGetValue(id, out UnitView u)) { sum += u.World; n++; }
        if (n == 0) return;
        Vector2 c = sum / n;
        if (embedded) { if (CameraController.Instance != null) CameraController.Instance.FocusOn(c); return; }
        cam.transform.position = new Vector3(c.x, c.y, -10f);
    }

    UnitView UnitAt(Vector2 world, int wantSide, bool onlyMine)
    {
        // Палец толще курсора: на телефоне ловим бойца шире
        float pick = Mathf.Max(UiKit.Phone ? 0.9f : 0.6f, cam.orthographicSize * (UiKit.Phone ? 0.045f : 0.025f));
        UnitView best = null;
        float bestD = pick;
        foreach (var u in units.Values)
        {
            if (onlyMine && !u.mine) continue;
            if (wantSide > 0 && u.side != wantSide) continue;
            if (wantSide < 0 && u.side == -wantSide) continue;
            float d = Vector2.Distance(u.World, world);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best;
    }

    BldView BuildingAt(Vector2 world)
    {
        Vector2 c = world - Origin;
        foreach (var b in blds.Values)
            if (!b.own && c.x >= b.d.x && c.x < b.d.x + b.d.w && c.y >= b.d.y && c.y < b.d.y + b.d.h) return b;
        return null;
    }

    void ClearSelection()
    {
        selected.Clear();
        selectedBld = selectedSrc = -1;
        panelMode = 0;
    }

    void Click(Vector2 world, bool shift, bool ctrl)
    {
        // Свой боец - выбрать (дважды - всех такого вида на экране)
        UnitView u = UnitAt(world, 0, true);
        if (u != null)
        {
            bool dbl = lastClickUnit == u.id && Time.unscaledTime - lastClickAt < 0.45f;
            lastClickAt = Time.unscaledTime;
            lastClickUnit = u.id;
            selectedBld = selectedSrc = -1;
            if (dbl)
            {
                foreach (var o in units.Values) if (o.mine && o.type == u.type && OnScreen(o.World, 0f)) selected.Add(o.id);
                return;
            }
            if (!shift) selected.Clear();
            if (shift && selected.Contains(u.id)) selected.Remove(u.id); else selected.Add(u.id);
            panelMode = 0;
            return;
        }
        // Бойцы выбраны - нажатие по земле или по врагу - приказ
        if (selected.Count > 0 && !shift)
        {
            Order(world, ctrl || plainMove);
            return;
        }
        if (!shift) selected.Clear();
        BldView b = BuildingAt(world);
        selectedBld = b != null ? b.d.id : -1;
        SrcView sv = b == null ? SourceAt(world) : null;
        selectedSrc = sv != null ? sv.d.id : -1;
    }

    void BoxSelect(Vector2 a, Vector2 b, bool shift)
    {
        Vector2 wa = MouseWorld(a), wb = MouseWorld(b);
        Rect r = Rect.MinMaxRect(Mathf.Min(wa.x, wb.x), Mathf.Min(wa.y, wb.y), Mathf.Max(wa.x, wb.x), Mathf.Max(wa.y, wb.y));
        if (!shift) selected.Clear();
        selectedBld = selectedSrc = -1;
        panelMode = 0;
        foreach (var u in units.Values) if (u.mine && r.Contains(u.World)) selected.Add(u.id);
    }

    void Order(Vector2 world, bool plain)
    {
        if (st == null || st.myCity < 0 || selected.Count == 0) return;
        var ids = new List<int>(selected);
        UnitView enemy = UnitAt(world, -st.mySide, false);
        if (enemy != null)
        {
            Send(new WarCommand { kind = WarCommand.Attack, units = ids, target = enemy.id });
            CombatFx.Ring(enemy.World, 0.6f, new Color(1f, 0.35f, 0.3f, 0.8f));
            return;
        }
        BldView b = BuildingAt(world);
        if (b != null && b.side != st.mySide && b.Alive)
        {
            Send(new WarCommand { kind = WarCommand.Attack, units = ids, target = -b.d.id - 1 });
            CombatFx.Ring(b.Center, Mathf.Max(b.d.w, b.d.h) * 0.6f, new Color(1f, 0.35f, 0.3f, 0.8f));
            return;
        }
        MoveOrder(world - Origin, plain);
    }

    void MoveOrder(Vector2 cell, bool plain)
    {
        if (st == null || st.myCity < 0 || selected.Count == 0) return;
        if (cell.x < 0 || cell.y < 0 || cell.x >= W || cell.y >= H) return;
        int zone = G.ZoneOfCell((int)cell.x, (int)cell.y);
        if (!WarRules.CanEnter(st.mySide, zone, Stage)) { Say(ZoneClosedText(zone), true); return; }
        Send(new WarCommand { kind = plain ? WarCommand.Move : WarCommand.AttackMove, units = new List<int>(selected), x = cell.x, y = cell.y });
        CombatFx.Ring(Origin + cell, 0.7f, plain ? new Color(0.5f, 1f, 0.5f, 0.8f) : new Color(1f, 0.85f, 0.4f, 0.8f));
    }

    string ZoneClosedText(int zone)
    {
        if (Stage < WarStage.Prepare) return "Война ещё не началась";
        if (Stage >= WarStage.Finished) return "Война окончена";
        if (zone == 0) return "Нейтральные земли откроются на 3-й день";
        if (zone != st.mySide) return "Земли врага откроются на 5-й день";
        return "Сюда пока нельзя";
    }

    // ---------- Отряды ----------

    string SquadName(int n)
    {
        if (n <= 0) return "Без отряда";
        string s = fr != null && fr.squadNames != null && n - 1 < fr.squadNames.Count ? fr.squadNames[n - 1] : null;
        if (!string.IsNullOrEmpty(s)) return Loc.Keep(s);
        return n - 1 < WarRules.SquadDefault.Length ? WarRules.SquadDefault[n - 1] : $"Отряд {n}";
    }

    int SquadCount(int n, out string topType)
    {
        int count = 0;
        var kinds = new Dictionary<string, int>();
        foreach (var u in units.Values)
        {
            if (!u.mine || (n >= 0 && u.squad != n)) continue;
            count++;
            kinds.TryGetValue(u.Def.name, out int k);
            kinds[u.Def.name] = k + 1;
        }
        topType = null;
        int best = 0;
        foreach (var kv in kinds) if (kv.Value > best) { best = kv.Value; topType = kv.Key; }
        return count;
    }

    /// <summary>Выбрать отряд n (-1 - всех). Уже выбран ровно он - камера к нему.</summary>
    void SelectSquad(int n)
    {
        var ids = new HashSet<int>();
        foreach (var u in units.Values) if (u.mine && (n < 0 || u.squad == n)) ids.Add(u.id);
        if (ids.Count == 0) { Say(n < 0 ? "У вас нет бойцов на карте — их учат казармы деревни" : $"В «{SquadName(n)}» нет бойцов", false); return; }
        if (ids.SetEquals(selected)) { FocusOn(ids); return; }
        selected.Clear();
        foreach (int id in ids) selected.Add(id);
        selectedBld = selectedSrc = -1;
        panelMode = 0;
    }

    void AssignSquad(int n)
    {
        if (selected.Count == 0) return;
        var ids = new List<int>(selected);
        foreach (int id in ids) if (units.TryGetValue(id, out UnitView u)) u.squad = n;
        squadLockUntil = Time.unscaledTime + 1.5f;   // пока сервер не ответил - не сбрасывать
        Send(new WarCommand { kind = WarCommand.SetSquad, units = ids, squad = n });
        Say(n == 0 ? $"Бойцы ({ids.Count}) теперь без отряда" : $"Бойцы ({ids.Count}) теперь в «{SquadName(n)}»", false);
        panelMode = 0;
    }

    /// <summary>Выбранные - ровно один отряд (для «Переименовать»): номер или -1.</summary>
    int SelectedSquad()
    {
        int sq = -1;
        foreach (int id in selected)
        {
            if (!units.TryGetValue(id, out UnitView u)) continue;
            if (sq == -1) sq = u.squad;
            else if (sq != u.squad) return -1;
        }
        return sq;
    }

    readonly Dictionary<string, UnitType> iconTypes = new Dictionary<string, UnitType>();

    void DrawUnitIcon(Rect r, string unitName)
    {
        if (string.IsNullOrEmpty(unitName) || Event.current.type != EventType.Repaint) return;
        if (!iconTypes.TryGetValue(unitName, out UnitType t)) { t = BarracksController.FindType(unitName); iconTypes[unitName] = t; }
        if (t != null) UnitIcons.Draw(r, t);
        else
        {
            Color old = GUI.color;
            GUI.color = MyColor;
            GUI.DrawTexture(new Rect(r.x + r.width * 0.25f, r.y + r.height * 0.25f, r.width * 0.5f, r.height * 0.5f), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }

    // ---------- Найм (то же, что проверяет сервер) ----------

    string CanTrain(WarUnitDef d)
    {
        WarCityView me = MyCity;
        if (me == null || me.fallen || (fr != null && fr.fallen)) return "Город пал";
        int barracks = 0;
        bool stable = false, eng = false;
        foreach (var b in blds.Values)
        {
            if (b.d.city != st.myCity || !b.Alive) continue;
            if (b.d.type == WarRules.BarracksName) barracks = Mathf.Max(barracks, b.d.level);
            if (b.d.type == WarRules.StableName) stable = true;
            if (b.d.type == WarRules.EngBarracksName) eng = true;
        }
        if (d.engineer) { if (!eng) return "Нужна Инженерная казарма"; }
        else if (barracks < d.barracks) return barracks == 0 ? "Нужна Казарма" : $"Нужна Казарма Ур.{d.barracks}";
        if (d.path != 0 && st.myPath != d.path) return d.path == 1 ? "Только путь Магии" : d.path == 2 ? "Только путь Военного дела" : "Только путь Инженерии";
        if (d.mounted && !stable) return "Нужна Конюшня";
        return null;
    }

    string Lack(int gold, int food, int wood, int stone, int iron)
    {
        if (fr == null) return "…";
        var p = new List<string>();
        if (fr.gold < gold) p.Add($"{gold - fr.gold} зол.");
        if (fr.food < food) p.Add($"{food - fr.food} еды");
        if (fr.wood < wood) p.Add($"{wood - fr.wood} дер.");
        if (fr.stone < stone) p.Add($"{stone - fr.stone} кам.");
        if (fr.iron < iron) p.Add($"{iron - fr.iron} жел.");
        return p.Count == 0 ? null : "Не хватает: " + string.Join(", ", p);
    }

    static string CostText(int gold, int food, int wood, int stone, int iron)
    {
        var p = new List<string>();
        if (gold > 0) p.Add(UiKit.Col(UiKit.GoldHex, gold + " з"));
        if (food > 0) p.Add(UiKit.Col(UiKit.FoodHex, food + " еды"));
        if (wood > 0) p.Add(UiKit.Col(UiKit.WoodHex, wood + " дер"));
        if (stone > 0) p.Add(UiKit.Col(UiKit.StoneHex, stone + " кам"));
        if (iron > 0) p.Add(UiKit.Col(IronHex, iron + " жел"));
        return p.Count == 0 ? UiKit.Col(UiKit.MutedHex, "бесплатно") : string.Join(" ", p);
    }

    void Train(WarUnitDef d, int count)
    {
        Send(new WarCommand { kind = WarCommand.Train, unit = d.name, count = count, squad = hireSquad },
             () => Say(count > 1 ? $"{d.name} ×{count}: в очереди" : $"{d.name}: в очереди", false));
    }

    // ---------- Экран: размеры ----------

    static float TopH => UiKit.Phone ? 44f : 48f;
    static float BarH => UiKit.Phone ? 56f : 52f;
    static float BarY => UiKit.H - BarH;
    static float TopBtnW => UiKit.Phone ? 78f : 98f;
    static float TopBtnTop => UiKit.Phone ? 5f : 9f;
    static float TopBtnHgt => UiKit.Phone ? 34f : 30f;

    static Rect ExitRect => new Rect(UiKit.W - 6f - TopBtnW, TopBtnTop, TopBtnW, TopBtnHgt);
    static Rect CityRect => new Rect(ExitRect.x - 6f - TopBtnW, TopBtnTop, TopBtnW, TopBtnHgt);
    /// <summary>Где на карте войны кнопка «Клан» (её рисует ClanWindow).</summary>
    public static Rect ClanButtonRect => new Rect(CityRect.x - 6f - TopBtnW, TopBtnTop, TopBtnW, TopBtnHgt);
    static Rect HelpBtnRect => new Rect(ClanButtonRect.x - 6f - 36f, TopBtnTop, 36f, TopBtnHgt);

    float MiniSize => embedded ? Mathf.Min(UiKit.Phone ? 130f : 190f, Mathf.Max(80f, UiKit.H - ChipRect.yMax - 130f))   // final-12-fix-1: меньше
                    : UiKit.Phone ? Mathf.Min(124f, (UiKit.H - TopH - BarH) * 0.42f) : Mathf.Min(220f, UiKit.H * 0.27f);
    Rect MiniRect
    {
        get
        {
            if (st == null || !worldReady || helpOpen || (embedded && (!chipOpen || giftOpen))) return new Rect(-9999f, -9999f, 0f, 0f);
            float s = MiniSize;
            float w = s, h = s;
            if (W > 0 && H > 0) { if (W >= H) h = s * H / W; else w = s * W / H; }
            if (embedded) return new Rect(ChipRect.xMax - w - 8f, ChipRect.yMax + 6f, w, h);
            return new Rect(UiKit.W - w - 10f, TopH + 8f, w, h);
        }
    }

    // ---------- На войне (вокруг деревни): что где ----------
    // Сверху слева - ресурсы деревни, сверху справа - кнопки деревни («Домой», «Клан»...). Под ними справа - плашка войны
    // (счёт, этап, «Вся карта», «Клану», «?»), развёрнутая - с миникартой. Снизу слева - отряды, над ними - приказы.
    // Справа внизу - кнопки деревни (полевые посты, переправы), туда не лезем.

    static float ChipW => UiKit.Phone ? 300f : 340f;
    static Rect ChipRect => new Rect(UiKit.W - 10f - ChipW, UiKit.TopBtnY + UiKit.TopBtnH + 8f, ChipW, UiKit.Phone ? 64f : 60f);
    static float EmbedBarW => Mathf.Max(300f, Mathf.Min(660f, UiKit.W - 380f));
    static float EmbedBarY => UiKit.H - 48f - (DevelopmentPath.IsCamo ? 36f : 0f);   // над кнопкой дозоров

    Rect BarRect => embedded ? new Rect(10f, EmbedBarY, EmbedBarW, 42f) : new Rect(0f, BarY, UiKit.W, BarH);

    Rect HireRect
    {
        get
        {
            float w = Mathf.Min(UiKit.Phone ? 430f : 470f, UiKit.W - MiniSize - 40f);
            float y = TopH + 6f;
            return new Rect(6f, y, w, BarY - 6f - y);
        }
    }

    /// <summary>Полоса приказов над полосой отрядов (слева её не закрывает окно найма).</summary>
    Rect ActionRect
    {
        get
        {
            if (embedded) { float eh = UiKit.Phone ? 42f : 38f; return new Rect(10f, EmbedBarY - 6f - eh, Mathf.Max(EmbedBarW, Mathf.Min(720f, UiKit.W - 20f - ChipW - 10f)), eh); }
            float x = hireOpen ? HireRect.xMax + 6f : 6f;
            float h = UiKit.Phone ? 42f : 38f;
            return new Rect(x, BarY - 6f - h, UiKit.W - 6f - x, h);
        }
    }

    GUIStyle midCenter, wrapCenter, oneLine, oneSmall, smallWrap, iconLabel;
    GUIStyle MidCenter => midCenter ?? (midCenter = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleCenter });
    GUIStyle WrapCenter => wrapCenter ?? (wrapCenter = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = true });
    GUIStyle OneLine => oneLine ?? (oneLine = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft });
    GUIStyle OneSmall => oneSmall ?? (oneSmall = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft });
    GUIStyle SmallWrap => smallWrap ?? (smallWrap = new GUIStyle(UiKit.Small) { wordWrap = true });
    GUIStyle IconLabel => iconLabel ?? (iconLabel = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, richText = true });

    // ---------- Экран ----------

    void OnGUI()
    {
        if (WarCityMode.Active) { UiKit.Apply(); DrawHomeButton(); }
        if (!open) return;
        UiKit.Apply();
        GUI.depth = -5;
        if (cam == null) return;
        if (Event.current.type == EventType.Layout) { uiRects.Clear(); uiRects.AddRange(uiRectsNext); uiRectsNext.Clear(); }
        if (embedded) { DrawEmbedded(); return; }

        if (st != null && worldReady)
        {
            DrawWorldLabels();
            DrawBox();
        }
        DrawTop();
        if (st != null && worldReady)
        {
            DrawFeed();
            DrawMini();
            DrawBar();
            if (selected.Count > 0) DrawActions();
            else if (selectedBld >= 0 || selectedSrc >= 0) DrawInfo();
            if (hireOpen && st.myCity >= 0) DrawHire();
            DrawMessage();
        }
        DrawBanner();
        if (helpOpen) DrawHelp();
    }

    // ---------- На войне: экран вокруг деревни ----------

    void DrawHomeButton()
    {
        GUI.depth = -6;
        if (UiKit.Button(HomeButtonRect, "Домой", !GameRestart.Running, UiKit.Kind.Normal, "Вернуться в домашнюю деревню. Военная деревня живёт и без вас: жители работают, бойцы держат приказы"))
            WarCityMode.Leave();
    }

    void DrawEmbedded()
    {
        // klan-voina-14: открыто окно деревни (клан, рынок, меню, путь...) поверх плашки - плашку, миникарту и события
        // не рисуем, иначе они лезут сквозь окно и нажимаются вместо него
        bool chipCovered = Covered(ChipRect);
        if (st != null && worldReady)
        {
            DrawWorldLabels();
            DrawBox();
            DrawWarFx();
        }
        if (!chipCovered) DrawChip();
        if (st != null && worldReady)
        {
            if (!giftOpen && !chipCovered) DrawFeed();
            if (!chipCovered) DrawMini();
            if (st.myCity >= 0 && !Covered(BarRect)) DrawBar();
            if (!Covered(ActionRect))
            {
                if (selected.Count > 0) DrawActions();
                else if (selectedBld >= 0 || selectedSrc >= 0) DrawInfo();
                else DrawRuinRow();
            }
            DrawMessage();
            if (giftOpen) DrawGift();
        }
        DrawBanner();
        DrawAnnounce();
        if (helpOpen) DrawHelp();
    }

    /// <summary>Поверх этого места открыто окно деревни (клан, рынок, меню «Город», путь, кузница, окно здания).</summary>
    static bool coverCheck;   // final-12-fix-1: идёт Covered - WarView.IsPointerOver (через AccountManager) не считает свои же плашки окном

    static bool Covered(Rect r)
    {
        Vector2[] pts = { new Vector2(r.x + 6f, r.y + 6f), new Vector2(r.xMax - 6f, r.y + 6f), r.center,
                          new Vector2(r.x + 6f, r.yMax - 6f), new Vector2(r.xMax - 6f, r.yMax - 6f) };
        coverCheck = true;
        try
        {
            foreach (var p in pts)
                if (ClanWindow.IsPointerOver(p) || MarketWindow.IsPointerOver(p) || DevelopmentPath.IsPointerOver(p) || WeaponForge.IsPointerOver(p)
                    || (GameMenu.Instance != null && GameMenu.Instance.IsPointerOver(p)) || AccountManager.IsPointerOver(p)
                    || BuildingPlacer.IsPointerOverPanel(p)) return true;
        }
        catch (System.Exception) { }
        finally { coverCheck = false; }
        return false;
    }

    /// <summary>
    /// klan-voina-14: сколько места снизу экрана занимают полосы войны (отряды и приказы) - окна зданий и жителей деревни
    /// кончаются выше (BuildingPlacer.PanelBottomMargin), чтобы не лезть на них.
    /// </summary>
    public static float PanelBottomReserve
    {
        get
        {
            if (!Embedded) return 0f;
            float eh = UiKit.Phone ? 42f : 38f;
            return UiKit.H - (EmbedBarY - 6f - eh) + 4f;
        }
    }

    /// <summary>klan-voina-14: левый край плашки войны справа вверху: сообщения деревни (WarManager) не заходят правее.</summary>
    public static float ChipLeft => Embedded ? ChipRect.x : UiKit.W;

    // Плашка войны: счёт, этап, кнопки. Нажать на счёт - развернуть (миникарта и события)
    void DrawChip()
    {
        Rect r = ChipRect;
        LocGUI.Box(r, GUIContent.none);
        Ui(r);
        float pad = 4f, lh = 22f;
        Rect line = new Rect(r.x + 8f, r.y + 2f, r.width - 16f, lh);
        string text;
        if (st == null || fr == null) text = UiKit.Col(UiKit.AccentHex, "Клановая война") + UiKit.Col(UiKit.MutedHex, building != null ? " · рисуем карту…" : " · загрузка…");
        else
        {
            int my = st.mySide == 1 ? fr.scoreA : fr.scoreB, their = st.mySide == 1 ? fr.scoreB : fr.scoreA;
            text = $"<b>{UiKit.Col(UiKit.GoodHex, my.ToString())} : {UiKit.Col(UiKit.BadHex, their.ToString())}</b>  " + UiKit.Col(UiKit.AccentHex, WarRules.StageShort((WarStage)fr.stage));
            if (fr.stage < (int)WarStage.Finished) text += UiKit.Col(UiKit.MutedHex, $" · ещё {Dur(fr.stageLeft)}");
        }
        LocGUI.Label(line, text + "  " + UiKit.Col(UiKit.MutedHex, chipOpen ? "▲" : "▼"), IconLabel);
        if (st != null && fr != null)
        {
            string me = st.mySide == 1 ? st.nameA : st.nameB, them = st.mySide == 1 ? st.nameB : st.nameA;
            UiKit.Tip(line, $"«{Loc.Keep(me)}» против «{Loc.Keep(them)}».\nСчёт: занятые участки + 2 за каждую целую деревню.\nСейчас: {WarRules.StageName((WarStage)fr.stage)}. До конца войны {Dur(fr.warLeft)}.\nНажмите — {(chipOpen ? "свернуть" : "миникарта и события")}");
        }
        if (GUI.Button(line, GUIContent.none, GUIStyle.none)) { chipOpen = !chipOpen; PlayerPrefs.SetInt("war.chipOpen", chipOpen ? 1 : 0); }

        float by = r.y + lh + 4f, bh = r.yMax - pad - by;
        float x = r.x + pad, w = r.width - pad * 2f;
        float b1 = (w - 8f - 36f) * 0.55f, b2 = w - 8f - 36f - b1;
        bool ready = st != null && worldReady;
        if (UiKit.Button(new Rect(x, by, b1, bh), ShowsWholeMap ? "К деревне" : "Вся карта", ready, UiKit.Kind.Normal,
                ShowsWholeMap ? "Вернуть камеру к своей деревне (Пробел)" : "Отдалить камеру: вся карта войны — свои, враги, источники"))
            ToggleWholeMap();
        bool canGift = ready && st.myCity >= 0;
        if (UiKit.Button(new Rect(x + b1 + 4f, by, b2, bh), "Клану", canGift, giftOpen ? UiKit.Kind.Primary : UiKit.Kind.Normal,
                "Помочь своим: подарить ресурсы соседу по клану или торговать на рынке клана"))
        { giftOpen = !giftOpen; if (giftOpen) ResetGift(); }
        if (UiKit.Button(new Rect(r.xMax - pad - 36f, by, 36f, bh), "?", UiKit.Kind.Ghost, "Как играть на войне")) helpOpen = !helpOpen;
    }

    // Выбрано своё здание, которое враги снесли на карте войны: его можно починить досками и камнем деревни
    BuildingInstance RuinedSelected()
    {
        BuildingInstance b = BuildingPlacer.Selected;
        return b != null && b.IsRuined && WarCityMode.IsRuinedByWar(b) ? b : null;
    }

    void DrawRuinRow()
    {
        BuildingInstance bi = RuinedSelected();
        if (bi == null) return;
        BldView v = OwnBuildingAt(bi.OriginCell);
        Rect r = ActionRect;
        LocGUI.Box(r, GUIContent.none);
        Ui(r);
        float pad = 4f, bh = r.height - pad * 2f, y = r.y + pad;
        float bw = UiKit.Phone ? 190f : 210f;
        float bx = r.xMax - pad - bw;
        string why = null;
        int wood = 0, stone = 0;
        if (v == null) why = "Этого здания нет на карте войны";
        else if (v.repairing) why = "Уже чинят: встанет, когда рядом не будет врагов";
        else if (v.d.type == WarRules.HallName) why = "Цитадель не чинят: деревня пала";
        else
        {
            WarRules.RepairCost(v.d.maxHp, true, out wood, out stone);
            ResourceManager rm = ResourceManager.Instance;
            if (rm != null && (rm.wood < wood || rm.stone < stone)) why = $"Не хватает: нужно {wood} дерева и {stone} камня";
        }
        string label = v != null && v.repairing ? "Чинят…" : "Починить: " + CostText(0, 0, wood, stone, 0);
        if (UiKit.Button(new Rect(bx, y, bw, bh), label, why == null, UiKit.Kind.Primary, why ?? "Доски и камень — из деревни. Здание встанет, когда рядом не будет врагов"))
        {
            int id = v.d.id;
            Send(new WarCommand { kind = WarCommand.Repair, building = id }, res =>
            {
                WarGrant g = res.warIncome;
                ResourceManager rm = ResourceManager.Instance;
                if (g != null && rm != null && WarCityMode.Active)
                {
                    CityIncome.Skip();
                    rm.wood = Mathf.Max(0, rm.wood - g.wood);
                    rm.stone = Mathf.Max(0, rm.stone - g.stone);
                    if (AccountManager.Instance != null) AccountManager.Instance.SaveCityNow(true);
                }
                Say("Ремонт начался: здание встанет, когда рядом не будет врагов", false);
            }, null);
        }
        string line1 = $"<b>{bi.type.name}</b> — {UiKit.Col(UiKit.BadHex, "враги снесли его на карте войны")}";
        string line2 = "Не работает и само не встанет. Почините — оно поднимется, когда рядом не будет врагов.";
        LocGUI.Label(new Rect(r.x + 8f, y, bx - r.x - 12f, bh / 2f + 2f), line1, IconLabel);
        LocGUI.Label(new Rect(r.x + 8f, y + bh / 2f, bx - r.x - 12f, bh / 2f), UiKit.Col(UiKit.MutedHex, line2), IconLabel);
        UiKit.Tip(new Rect(r.x, r.y, bx - r.x, r.height), line2);
    }

    // ---------- «Клану»: подарки соседям и рынок клана ----------

    static readonly string[] GiftNames = { "Золото", "Еда", "Дерево", "Камень", "Железо" };
    static readonly int[] GiftStep = { 50, 50, 50, 50, 10 };

    void ResetGift()
    {
        for (int i = 0; i < gift.Length; i++) gift[i] = 0;
        List<WarSlot> mates = fr != null ? fr.mates : null;
        if (mates == null || !mates.Exists(m => m != null && m.accountId == giftTo)) giftTo = null;
        if (giftTo == null && mates != null && mates.Count > 0)
        {
            giftTo = mates[0].accountId;
        }
    }

    static int Have(int i)
    {
        ResourceManager rm = ResourceManager.Instance;
        if (rm == null) return 0;
        switch (i) { case 0: return rm.gold; case 1: return rm.food; case 2: return rm.wood; case 3: return rm.stone; default: return rm.iron; }
    }

    static string GiftHex(int i) { return i == 0 ? UiKit.GoldHex : i == 1 ? UiKit.FoodHex : i == 2 ? UiKit.WoodHex : i == 3 ? UiKit.StoneHex : IronHex; }

    void DrawGift()
    {
        float w = Mathf.Min(560f, UiKit.W - 20f), h = Mathf.Min(UiKit.Phone ? UiKit.H - 16f : 380f, UiKit.H - 16f);
        Rect r = new Rect((UiKit.W - w) / 2f, (UiKit.H - h) / 2f, w, h);
        Rect c = UiKit.Window(r, "Помочь своим", out bool close);
        Ui(r);
        if (close) { giftOpen = false; return; }
        List<WarSlot> mates = fr != null ? fr.mates : null;
        float btnH = UiKit.Phone ? 36f : 32f;
        float y = c.y;

        // Кому
        float lw = Mathf.Min(190f, c.width * 0.36f);
        LocGUI.Label(new Rect(c.x, y, lw, 20f), UiKit.Col(UiKit.MutedHex, "Кому:"), OneSmall);
        Rect list = new Rect(c.x, y + 22f, lw, c.yMax - btnH - 8f - (y + 22f));
        if (mates == null || mates.Count == 0)
            LocGUI.Label(list, UiKit.Col(UiKit.MutedHex, "Соседей по клану на этой войне нет"), SmallWrap);
        else
        {
            float rowH = UiKit.Phone ? 34f : 28f;
            UiKit.Scroll("war.gift.to", list, mates.Count * (rowH + 4f), cc =>
            {
                for (int i = 0; i < mates.Count; i++)
                {
                    WarSlot m = mates[i];
                    if (m == null) continue;
                    bool on = m.accountId == giftTo;
                    if (UiKit.Button(new Rect(cc.x, cc.y + i * (rowH + 4f), cc.width, rowH), Loc.Keep(m.name), on ? UiKit.Kind.Primary : UiKit.Kind.Normal, null)) giftTo = m.accountId;
                }
            });
        }

        // Сколько
        float rx = c.x + lw + 12f, rw = c.xMax - rx;
        LocGUI.Label(new Rect(rx, y, rw, 20f), UiKit.Col(UiKit.MutedHex, "Что подарить (из запасов деревни):"), OneSmall);
        float rowY = y + 24f, rh = UiKit.Phone ? 34f : 30f;
        float sb = UiKit.Phone ? 40f : 36f;
        for (int i = 0; i < 5; i++)
        {
            Rect row = new Rect(rx, rowY + i * (rh + 4f), rw, rh);
            int have = Have(i);
            gift[i] = Mathf.Clamp(gift[i], 0, have);
            float bxr = row.xMax;
            if (UiKit.Button(new Rect(bxr - sb, row.y, sb, rh), "½", have > 1, UiKit.Kind.Ghost, "Половину запаса")) gift[i] = have / 2;
            bxr -= sb + 4f;
            if (UiKit.Button(new Rect(bxr - sb, row.y, sb, rh), "+", gift[i] < have, UiKit.Kind.Normal, $"+{GiftStep[i]}")) gift[i] = Mathf.Min(have, gift[i] + GiftStep[i]);
            bxr -= sb + 4f;
            if (UiKit.Button(new Rect(bxr - sb, row.y, sb, rh), "−", gift[i] > 0, UiKit.Kind.Normal, $"−{GiftStep[i]}")) gift[i] = Mathf.Max(0, gift[i] - GiftStep[i]);
            bxr -= sb + 8f;
            LocGUI.Label(new Rect(row.x, row.y, bxr - row.x, rh),
                $"{GiftNames[i]}: <b>{UiKit.Col(GiftHex(i), gift[i].ToString())}</b>" + UiKit.Col(UiKit.MutedHex, $"  из {have}"), IconLabel);
        }
        float noteY = rowY + 5f * (rh + 4f);
        if (noteY + 34f < c.yMax - btnH - 6f)
            LocGUI.Label(new Rect(rx, noteY, rw, c.yMax - btnH - 6f - noteY), UiKit.Col(UiKit.MutedHex,
                "Подарок придёт сразу, если сосед сейчас на войне, иначе — когда он туда зайдёт. Помогайте деревням у края карты: им первым достаётся от врага."), SmallWrap);

        // Кнопки
        int total = 0;
        foreach (int v in gift) total += v;
        WarSlot to = mates != null ? mates.Find(m => m != null && m.accountId == giftTo) : null;
        float gw = Mathf.Min(220f, (c.width - 8f) / 2f);
        string why = to == null ? "Выберите, кому" : total <= 0 ? "Выберите, что подарить" : null;
        if (UiKit.Button(new Rect(c.x, c.yMax - btnH, gw, btnH), to != null ? $"Подарить: {Loc.Keep(to.name)}" : "Подарить", why == null, UiKit.Kind.Primary, why))
            SendGift(to);
        if (UiKit.Button(new Rect(c.xMax - gw, c.yMax - btnH, gw, btnH), "Рынок клана", UiKit.Kind.Normal,
                "Продать и купить у своих: ресурсы, оружие, бойцов. Деньги — в деревню"))
        { giftOpen = false; ClanWarMarket.Open(); }
    }

    void SendGift(WarSlot to)
    {
        ResourceManager rm = ResourceManager.Instance;
        if (rm == null || to == null) return;
        int[] a = (int[])gift.Clone();
        for (int i = 0; i < 5; i++) if (a[i] > Have(i)) { Say("В деревне столько нет", true); return; }
        // Списываем сразу (не подарить дважды), не дошло - вернём
        if (!rm.Spend(a[0], a[2], a[3], a[1]) || !rm.SpendIron(a[4])) { Say("В деревне столько нет", true); return; }
        string name = Loc.Keep(to.name);
        giftOpen = false;
        Send(new WarCommand { kind = WarCommand.Gift, memberId = to.accountId, gold = a[0], food = a[1], wood = a[2], stone = a[3], iron = a[4] },
            _ =>
            {
                WarManager.Notify($"Подарок для {name}: " + WarRules.IncomeText(a), true);
                if (AccountManager.Instance != null) AccountManager.Instance.SaveCityNow(true);
            },
            res =>
            {
                CityIncome.Skip();
                if (ResourceManager.Instance != null) ResourceManager.Instance.Add(a[0], a[2], a[3], a[1], a[4]);
                WarManager.Notify("Подарок не ушёл: " + res.error, false);
            });
    }

    static string Dur(float s)
    {
        int t = Mathf.Max(0, Mathf.CeilToInt(s));
        int d = t / 86400, h = t % 86400 / 3600, m = t % 3600 / 60, sec = t % 60;
        if (d > 0) return $"{d} д {h} ч";
        if (h > 0) return $"{h} ч {m} мин";
        if (m > 0) return $"{m} мин {sec} с";
        return $"{sec} с";
    }

    void DrawTop()
    {
        Rect r = new Rect(0f, 0f, UiKit.W, TopH);
        LocGUI.Box(r, GUIContent.none);
        Ui(r);
        float right = HelpBtnRect.x - 8f;
        float x = 10f;

        if (UiKit.Button(HelpBtnRect, "?", UiKit.Kind.Ghost, "Как играть на войне")) helpOpen = !helpOpen;
        if (UiKit.Button(CityRect, "Город", st != null && worldReady, UiKit.Kind.Normal, "Показать свой город (Пробел)")) FocusMyCity(false);
        if (UiKit.Button(ExitRect, "Выйти", UiKit.Kind.Normal, "Вернуться в свою деревню. Война идёт дальше без вас, бойцы держат приказы")) { DoClose(); return; }

        if (st == null) { LocGUI.Label(new Rect(x, 0f, right - x, TopH), UiKit.Col(UiKit.AccentHex, "Клановая война"), OneLine); return; }
        float lh = TopH / 2f;
        string me = st.mySide == 1 ? st.nameA : st.nameB, them = st.mySide == 1 ? st.nameB : st.nameA;
        int my = fr == null ? 0 : st.mySide == 1 ? fr.scoreA : fr.scoreB, their = fr == null ? 0 : st.mySide == 1 ? fr.scoreB : fr.scoreA;
        string line1 = $"<b>«{Loc.Keep(me)}»</b> {UiKit.Col(UiKit.GoodHex, my.ToString())} : {UiKit.Col(UiKit.BadHex, their.ToString())} <b>«{Loc.Keep(them)}»</b>";
        if (fr != null)
        {
            line1 += "   " + UiKit.Col(UiKit.AccentHex, WarRules.StageShort((WarStage)fr.stage));
            if (fr.stage < (int)WarStage.Finished) line1 += UiKit.Col(UiKit.MutedHex, $" · ещё {Dur(fr.stageLeft)}");
            if (st.fast) line1 += UiKit.Col(UiKit.WarnHex, " · быстрая");
        }
        LocGUI.Label(new Rect(x, 1f, right - x, lh), line1, OneLine);
        Rect tipLine1 = new Rect(x, 1f, Mathf.Min(right - x, 420f), lh);
        if (fr != null) UiKit.Tip(tipLine1, $"Счёт: занятые участки + 2 за каждый целый город.\nСейчас: {WarRules.StageName((WarStage)fr.stage)}. До конца войны {Dur(fr.warLeft)}.\nПобеда приносит рейтинг вашему клану.");

        if (fr != null && st.myCity >= 0)
            LocGUI.Label(new Rect(x, lh - 1f, right - x, lh), UiKit.Col(UiKit.MutedHex, "Ваша деревня — на этой войне: «На войну» в окне клана, и она живёт посреди карты"), OneSmall);
        else if (st.myCity < 0) LocGUI.Label(new Rect(x, lh - 1f, right - x, lh), UiKit.Col(UiKit.MutedHex, "Вашего города нет на этой войне — можно только смотреть"), OneSmall);
    }

    void DrawFeed()
    {
        float now = Time.unscaledTime;
        float x = hireOpen ? HireRect.xMax + 8f : 8f;
        float w = Mathf.Min(480f, MiniRect.x - x - 10f);
        int max = UiKit.Phone ? 4 : 6;
        float y = TopH + 6f;
        if (embedded)
        {
            // На войне - под плашкой войны (развёрнута - под миникартой)
            x = ChipRect.x;
            w = ChipW;
            y = MiniRect.width > 0f ? MiniRect.yMax + 6f : ChipRect.yMax + 4f;
            max = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(ActionRect.y, UiKit.H - 110f) - y) / 19f), 0, chipOpen ? (UiKit.Phone ? 3 : 6) : 2);
        }
        if (w < 140f || max <= 0) return;
        Color old = GUI.color;
        int shown = 0;
        for (int i = feed.Count - 1; i >= 0 && shown < max; i--)
        {
            float age = now - feed[i].at;
            if (age > 30f) break;
            float a = Mathf.Clamp01(1.5f - age / 20f);
            Rect r = new Rect(x, y + shown * 19f, w, 18f);
            GUI.color = new Color(0f, 0f, 0f, 0.45f * a);
            if (e.type == EventType.Repaint) GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, a);
            LocGUI.Label(new Rect(r.x + 4f, r.y, r.width - 8f, r.height), feed[i].text, OneSmall);
            shown++;
        }
        GUI.color = old;
    }

    static Event e => Event.current;

    void DrawMessage()
    {
        if (Time.unscaledTime >= msgUntil || string.IsNullOrEmpty(msg)) return;
        bool row = selected.Count > 0 || selectedBld >= 0 || selectedSrc >= 0 || (embedded && RuinedSelected() != null);
        float y = (row ? ActionRect.y : BarRect.y) - 30f;
        float x0 = embedded ? 10f : hireOpen ? HireRect.xMax + 6f : 6f;
        Rect r = new Rect(x0, y, embedded ? ActionRect.width : UiKit.W - x0 - 6f, 26f);
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        float tw = Mathf.Min(r.width, UiKit.Body.CalcSize(new GUIContent(msg)).x + 30f);
        if (e.type == EventType.Repaint) GUI.DrawTexture(new Rect(r.center.x - tw / 2f, r.y, tw, r.height), Texture2D.whiteTexture);
        GUI.color = old;
        LocGUI.Label(r, UiKit.Col(msgBad ? UiKit.BadHex : UiKit.GoodHex, msg), MidCenter);
    }

    // Полоса отрядов внизу: «Найм», «Все», отряды
    void DrawBar()
    {
        Rect r = BarRect;
        LocGUI.Box(r, GUIContent.none);
        Ui(r);
        float pad = embedded ? 4f : 5f, bh = r.height - pad * 2f, y = r.y + pad;
        float x = r.x + 6f;
        bool mine = st.myCity >= 0;
        // final-13-fix-2: в деревне войны палец всегда двигает карту, как в обычной деревне - кнопки «Рамка» там нет
        if (embedded) TouchControls.BoxSelect = false;
        bool boxBtn = UiKit.Phone && !embedded;
        bool box = TouchControls.BoxSelect;
        float boxW = boxBtn ? 82f : 0f;
        float right = r.xMax - 6f - (boxBtn ? boxW + 6f : 0f);
        if (boxBtn && UiKit.Button(new Rect(right + 6f, y, boxW, bh), box ? "Рамка: да" : "Рамка", box ? UiKit.Kind.Primary : UiKit.Kind.Normal,
                "Нажмите и проведите пальцем по карте — выберете всех своих бойцов в рамке. Потом палец снова двигает карту"))
            TouchControls.BoxSelect = !box;

        if (!mine) return;
        // Какие отряды показать: «Все» и отряды, где есть бойцы
        var list = new List<int> { -1 };
        for (int n = 1; n <= WarRules.Squads; n++) if (SquadCount(n, out _) > 0) list.Add(n);
        if (SquadCount(0, out _) > 0) list.Add(0);
        int total = SquadCount(-1, out _);
        if (total == 0)
        {
            LocGUI.Label(new Rect(x + 4f, y, right - x - 4f, bh), UiKit.Col(UiKit.MutedHex, "Бойцов на карте нет. Их учат казармы деревни — обученные сами выходят на карту"), OneSmall);
            return;
        }
        float bw = Mathf.Clamp((right - x) / list.Count - 4f, UiKit.Phone ? 64f : 70f, 150f);
        foreach (int n in list)
        {
            if (x + bw > right + 0.5f) break;
            int count = SquadCount(n, out string top);
            bool on = n < 0 ? selected.Count == total : IsSquadSelected(n, count);
            string name = n < 0 ? "Все" : SquadName(n);
            string key = n < 0 ? "Ctrl+A" : n.ToString();
            string tip = n < 0 ? $"Выбрать всех бойцов ({key}). Ещё раз — показать их"
                               : $"Выбрать «{name}» ({key}). Ещё раз — показать на карте.\nВыбрать бойцов и нажать «В отряд» — собрать свой отряд";
            Rect b = new Rect(x, y, bw, bh);
            if (UiKit.Button(b, "", on ? UiKit.Kind.Primary : UiKit.Kind.Normal, tip)) SelectSquad(n);
            float ic = bh - 8f;
            if (n < 0)
            {
                LocGUI.Label(new Rect(b.x + 6f, b.y, b.width - 12f, b.height), $"<b>Все</b> {UiKit.Col(UiKit.MutedHex, "×" + count)}", new GUIStyle(IconLabel) { alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                DrawUnitIcon(new Rect(b.x + 3f, b.y + 4f, ic, ic), top);
                float tx = b.x + ic + 6f, tw = b.xMax - tx - 4f;
                LocGUI.Label(new Rect(tx, b.y + 2f, tw, bh / 2f), $"<b>{name}</b>", IconLabel);
                LocGUI.Label(new Rect(tx, b.y + bh / 2f - 2f, tw, bh / 2f), UiKit.Col(UiKit.MutedHex, $"×{count}"), IconLabel);
            }
            x += bw + 4f;
        }
    }

    bool IsSquadSelected(int n, int count)
    {
        if (selected.Count == 0 || selected.Count != count) return false;
        foreach (int id in selected) if (!units.TryGetValue(id, out UnitView u) || u.squad != n) return false;
        return true;
    }

    // Приказы выбранным - над полосой отрядов
    void DrawActions()
    {
        Rect r = ActionRect;
        LocGUI.Box(r, GUIContent.none);
        Ui(r);
        float pad = 4f, bh = r.height - pad * 2f, y = r.y + pad;
        float bx = r.xMax - pad;

        Rect B(float w) { bx -= w; Rect b = new Rect(bx, y, w, bh); bx -= 4f; return b; }

        if (panelMode == 1)
        {
            // В какой отряд
            // Девять кнопок (Назад, Имя, Без, 6..1) - ширина по месту, чтобы 1..6 влезли всегда
            float w = Mathf.Clamp((r.width - 8f - 9f * 4f - 30f) / 9.4f, 36f, 70f);
            if (UiKit.Button(B(w), "Назад", UiKit.Kind.Ghost, "Esc")) panelMode = 0;
            int cur = SelectedSquad();
            if (cur > 0 && UiKit.Button(B(w * 1.4f), "Имя…", UiKit.Kind.Normal, $"Переименовать «{SquadName(cur)}»"))
            { panelMode = 2; renameSquad = cur; renameText = fr != null && fr.squadNames != null && cur - 1 < fr.squadNames.Count ? fr.squadNames[cur - 1] ?? "" : ""; }
            if (UiKit.Button(B(w), "Без", UiKit.Kind.Normal, "Убрать из отрядов (Ctrl+0)")) AssignSquad(0);
            for (int n = WarRules.Squads; n >= 1; n--)
            {
                Rect b = B(w);
                if (UiKit.Button(b, n.ToString(), cur == n ? UiKit.Kind.Primary : UiKit.Kind.Normal, $"В «{SquadName(n)}» (Ctrl+{n})")) AssignSquad(n);
            }
            float lw = bx - r.x - 8f;
            if (lw > 60f) LocGUI.Label(new Rect(r.x + 8f, y, lw, bh), UiKit.Col(UiKit.MutedHex, $"Выбрано {selected.Count}: в какой отряд?"), OneSmall);
            return;
        }
        if (panelMode == 2)
        {
            if (UiKit.Button(B(UiKit.Phone ? 76f : 86f), "Отмена", UiKit.Kind.Ghost, null)) panelMode = 0;
            bool ok = UiKit.Button(B(UiKit.Phone ? 70f : 80f), "OK", UiKit.Kind.Primary, null);
            float fx = r.x + 8f + (UiKit.Phone ? 90f : 110f);
            LocGUI.Label(new Rect(r.x + 8f, y, fx - r.x - 12f, bh), UiKit.Col(UiKit.MutedHex, $"Отряд {renameSquad}:"), OneSmall);
            renameText = MobileInput.Field(new Rect(fx, y + 2f, Mathf.Max(80f, bx - fx - 4f), bh - 4f), "war.squadname", renameText, 18, "Название отряда");
            UiKit.MarkTextWindow();
            if (ok || MobileInput.Submitted("war.squadname") || (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)))
            {
                string name = (renameText ?? "").Trim();
                int sq = renameSquad;
                Send(new WarCommand { kind = WarCommand.RenameSquad, squad = sq, unit = name }, () => Say(name == "" ? $"Отряду {sq} вернули название" : $"Отряд {sq} теперь «{name}»", false));
                if (fr != null && fr.squadNames != null && sq - 1 < fr.squadNames.Count) fr.squadNames[sq - 1] = name;
                panelMode = 0;
                GUIUtility.keyboardControl = 0;
            }
            return;
        }

        float w1 = UiKit.Phone ? 84f : 96f;
        if (UiKit.Button(B(40f), "✕", UiKit.Kind.Ghost, "Снять выбор (Esc)")) { ClearSelection(); return; }
        if (UiKit.Button(B(w1 + 6f), "В отряд…", UiKit.Kind.Normal, "Собрать выбранных в отряд 1–6 (Ctrl+цифра) или переименовать отряд")) panelMode = 1;
        if (UiKit.Button(B(w1 + 24f), plainMove ? "Просто идти" : "Идти с боем", plainMove ? UiKit.Kind.Primary : UiKit.Kind.Normal,
                plainMove ? "Сейчас: нажатие по земле — просто идти, не отвлекаясь на врагов. Нажмите — идти с боем"
                          : "Сейчас: нажатие по земле — идти и бить всех врагов по пути. Нажмите — просто идти" + (UiKit.Phone ? "\nДолгое нажатие по земле — всегда «просто идти»" : "\nCtrl+щелчок — просто идти")))
            plainMove = !plainMove;
        if (UiKit.Button(B(w1), "Стоять", UiKit.Kind.Normal, "Стоять на месте и не гоняться за врагами — только бить тех, кто подошёл (H)"))
            Send(new WarCommand { kind = WarCommand.Hold, units = new List<int>(selected) }, () => Say("Стоят на месте", false));
        if (UiKit.Button(B(w1 - 10f), "Стоп", UiKit.Kind.Normal, "Остановиться (S). Сами отвечают врагам рядом"))
            Send(new WarCommand { kind = WarCommand.Stop, units = new List<int>(selected) });
        // klan-voina-14: у своей казармы - зайти в неё (там меняют снаряжение, как дома)
        if (embedded)
        {
            List<int> near = SelectedNearHome(out BarracksController nearBr);
            if (near.Count > 0 && nearBr != null && UiKit.Button(B(w1 + 18f), $"В казарму ({near.Count})", UiKit.Kind.Primary,
                    "Бойцы у своей казармы заходят в неё и ждут приказа. В казарме им меняют оружие, щит и броню"))
            {
                WarFieldSoldiers.Recall(nearBr, near, false);
                foreach (int id in near) selected.Remove(id);
            }
        }

        // Слева - кто выбран
        float lw2 = bx - r.x - 8f;
        if (lw2 < 70f) return;
        var count = new Dictionary<string, int>();
        int hp = 0, hold = 0;
        string top = null;
        int topN = 0;
        foreach (int id in selected)
            if (units.TryGetValue(id, out UnitView u))
            {
                count.TryGetValue(u.Def.name, out int n);
                count[u.Def.name] = ++n;
                if (n > topN) { topN = n; top = u.Def.name; }
                hp += u.hp;
                if (u.hold) hold++;
            }
        float ic = bh - 4f;
        DrawUnitIcon(new Rect(r.x + 6f, y + 2f, ic, ic), top);
        var parts = new List<string>();
        foreach (var kv in count) parts.Add($"{kv.Key} ×{kv.Value}");
        string line = $"<b>Выбрано {selected.Count}</b>" + UiKit.Col(UiKit.MutedHex, $" · здоровье {hp / Mathf.Max(1, selected.Count)}%" + (hold > 0 ? " · стоят" : ""));
        float tx = r.x + 10f + ic;
        LocGUI.Label(new Rect(tx, y, bx - tx - 4f, bh / 2f + 2f), line, IconLabel);
        LocGUI.Label(new Rect(tx, y + bh / 2f, bx - tx - 4f, bh / 2f), UiKit.Col(UiKit.MutedHex, string.Join(", ", parts)), IconLabel);
        UiKit.Tip(new Rect(r.x, r.y, bx - r.x, r.height), string.Join("\n", parts) + "\n\n" + (UiKit.Phone
            ? "Нажмите по земле — идти туда, по врагу или его зданию — напасть."
            : "ЛКМ или ПКМ по земле — идти туда, по врагу или его зданию — напасть. Ctrl — просто идти."));
    }

    // Выбрано здание или источник - что это и что с ним можно сделать
    void DrawInfo()
    {
        Rect r = ActionRect;
        BldView b = null;
        SrcView sv = null;
        if (selectedBld >= 0 && !blds.TryGetValue(selectedBld, out b)) { selectedBld = -1; return; }
        if (b == null) { sv = srcs.Find(x => x.d.id == selectedSrc); if (sv == null) { selectedSrc = -1; return; } }

        string line1, line2;
        bool repair = false;
        int rw = 0, rs = 0;
        if (b != null)
        {
            WarCityView city = City(b.d.city);
            bool mine = b.d.city == st.myCity;
            string owner = city != null ? Loc.Keep(city.name) : "?";
            string state = !b.Alive ? UiKit.Col(UiKit.BadHex, "разрушено") : b.hp < 100 ? $"здоровье {b.hp}%" : "целое";
            if (b.repairing) state += UiKit.Col(UiKit.GoodHex, " · чинят");
            line1 = $"<b>{b.d.type}</b> ур.{b.d.level} · {UiKit.Col(b.side == st.mySide ? UiKit.InfoHex : UiKit.BadHex, mine ? "ваш город" : "город " + owner)} · {state}";
            line2 = WarRules.Shoots(b.d.type, b.d.level, out float range, out float dmg, out _, out _) ? $"Стреляет: дальность {range:0.#}, урон {dmg:0}. " : "";
            var inc = new int[5];
            WarRules.Income(b.d.type, b.d.level, inc);
            string it = WarRules.IncomeText(inc);
            if (it != "") line2 += $"Доход: {it} в минуту. ";
            if (b.d.type == WarRules.HallName) line2 += "Снесут Цитадель — город падёт.";
            if (mine && b.hp < 100 && !b.repairing && !(b.d.type == WarRules.HallName && !b.Alive))
            {
                float missing = b.d.maxHp * (100 - b.hp) / 100f;
                WarRules.RepairCost(missing, !b.Alive, out rw, out rs);
                repair = true;
            }
        }
        else
        {
            int o = SourceOwner(sv.d);
            string who = o == 0 ? UiKit.Col(UiKit.AccentHex, "ничей") : o == st.mySide ? UiKit.Col(UiKit.InfoHex, "ваш клан") : UiKit.Col(UiKit.BadHex, "у врага");
            var add = new int[5];
            WarRules.SourceIncome(sv.d.kind, add);
            if (st.fast) for (int i = 0; i < add.Length; i++) add[i] = Mathf.RoundToInt(add[i] * WarRules.FastIncomeMul);
            line1 = $"<b>{WarRules.SourceName(sv.d.kind)}</b> · {who} · клану {WarRules.IncomeText(add)} в минуту";
            line2 = (st.myCity >= 0 ? $"Вашему городу — {WarRules.IncomeText(add, AliveCities(st.mySide))} (делится между городами). " : "")
                  + (o == st.mySide ? "Держите участок: враг простоит минуту без ваших бойцов — заберёт." : "Чтобы забрать: простойте минуту на участке без врагов рядом.");
        }

        LocGUI.Box(r, GUIContent.none);
        Ui(r);
        float pad = 4f, bh = r.height - pad * 2f, y = r.y + pad;
        float bx = r.xMax - pad - 40f;
        if (UiKit.Button(new Rect(bx, y, 40f, bh), "✕", UiKit.Kind.Ghost, "Закрыть (Esc)")) { ClearSelection(); return; }
        if (repair)
        {
            float w = UiKit.Phone ? 150f : 170f;
            bx -= w + 4f;
            string lack = Lack(0, 0, rw, rs, 0);
            if (UiKit.Button(new Rect(bx, y, w, bh), "Чинить: " + CostText(0, 0, rw, rs, 0), lack == null, UiKit.Kind.Primary,
                    lack ?? "Чинят постепенно, пока рядом нет врагов. Разрушенное здание встанет снова"))
                Send(new WarCommand { kind = WarCommand.Repair, building = b.d.id }, () => Say("Ремонт начался", false));
        }
        LocGUI.Label(new Rect(r.x + 8f, y, bx - r.x - 12f, bh / 2f + 2f), line1, IconLabel);
        LocGUI.Label(new Rect(r.x + 8f, y + bh / 2f, bx - r.x - 12f, bh / 2f), UiKit.Col(UiKit.MutedHex, line2), IconLabel);
        UiKit.Tip(new Rect(r.x, r.y, bx - r.x, r.height), line2);
    }

    // ---------- Окно города: найм, улучшения, ремонт ----------

    static readonly string[] HireTabs = { "Найм", "Улучшения", "Ремонт" };

    void DrawHire()
    {
        Rect r = HireRect;
        WarCityView me = MyCity;
        string title = me != null ? $"Город «{Loc.Keep(me.name)}»" + (me.fallen || (fr != null && fr.fallen) ? UiKit.Col(UiKit.BadHex, " — пал") : "") : "Мой город";
        Rect c = UiKit.Window(r, title, out bool close);
        Ui(r);
        if (close) { hireOpen = false; return; }
        if (fr == null) { LocGUI.Label(c, UiKit.Col(UiKit.MutedHex, "Загрузка…"), UiKit.Body); return; }
        hireTab = UiKit.Tabs(new Rect(c.x, c.y, c.width, 28f), HireTabs, hireTab);
        Rect view = new Rect(c.x, c.y + 34f, c.width, c.yMax - c.y - 34f);
        switch (hireTab)
        {
            case 0: DrawTrainTab(view); break;
            case 1: DrawResearchTab(view); break;
            default: DrawRepairTab(view); break;
        }
    }

    void DrawTrainTab(Rect view)
    {
        float rowH = UiKit.Phone ? 50f : 46f;
        int queued = fr.queue != null ? fr.queue.Count : 0;
        float head = 24f + 30f + (queued > 0 ? 54f : 0f) + 6f;
        float contentH = head + WarRules.Units.Length * (rowH + 4f) + 8f;
        UiKit.Scroll("war.train", view, contentH, c =>
        {
            float y = c.y;
            float w = c.width;
            LocGUI.Label(new Rect(c.x, y, w, 22f), UiKit.Col(UiKit.MutedHex, $"Бойцов {fr.army}/{fr.armyCap} · места в казармах {fr.space}/{fr.spaceCap}"), OneSmall);
            UiKit.Tip(new Rect(c.x, y, w, 22f), "Мест больше, если в городе больше казарм и они выше уровнем. Крупные бойцы занимают несколько мест.");
            y += 24f;
            // Куда идут новые бойцы
            float lw = 70f;
            LocGUI.Label(new Rect(c.x, y, lw, 26f), UiKit.Col(UiKit.MutedHex, "В отряд:"), OneSmall);
            float bw = (w - lw) / (WarRules.Squads + 1) - 3f;
            for (int n = 0; n <= WarRules.Squads; n++)
            {
                Rect b = new Rect(c.x + lw + n * (bw + 3f), y, bw, 26f);
                if (UiKit.Button(b, n == 0 ? "—" : n.ToString(), hireSquad == n ? UiKit.Kind.Primary : UiKit.Kind.Normal, n == 0 ? "Новые бойцы — без отряда" : $"Новые бойцы — в «{SquadName(n)}»")) hireSquad = n;
            }
            y += 30f;
            if (queued > 0)
            {
                var q = fr.queue[0];
                var names = new List<string>();
                for (int i = 1; i < fr.queue.Count && i < 6; i++) names.Add(fr.queue[i].unit);
                string more = fr.queue.Count > 6 ? $" и ещё {fr.queue.Count - 6}" : "";
                LocGUI.Label(new Rect(c.x, y, w - 110f, 22f), $"Учится: <b>{q.unit}</b> {UiKit.Col(UiKit.MutedHex, $"({Mathf.CeilToInt(q.left)} с)")}", OneSmall);
                if (UiKit.Button(new Rect(c.xMax - 104f, y, 104f, 24f), "Отменить", UiKit.Kind.Ghost, "Отменить последнего в очереди (ресурсы вернутся)"))
                    Send(new WarCommand { kind = WarCommand.CancelTrain });
                if (names.Count > 0) LocGUI.Label(new Rect(c.x, y + 24f, w, 22f), UiKit.Col(UiKit.MutedHex, "Дальше: " + string.Join(", ", names) + more), OneSmall);
                y += 54f;
            }
            y += 6f;
            for (int i = 0; i < WarRules.Units.Length; i++)
            {
                WarUnitDef d = WarRules.Units[i];
                Rect row = new Rect(c.x, y, w, rowH);
                DrawTrainRow(row, d);
                y += rowH + 4f;
            }
        });
    }

    void DrawTrainRow(Rect row, WarUnitDef d)
    {
        int food = d.food + (d.mounted ? WarRules.HorseFood : 0);
        string why = CanTrain(d);
        if (why == null && fr.army >= fr.armyCap) why = $"Больше {fr.armyCap} бойцов у города быть не может";
        if (why == null && fr.space + d.space > fr.spaceCap) why = "Нет места в казармах";
        string lack = why == null ? Lack(d.gold, food, d.wood, 0, d.iron) : null;
        bool can = why == null && lack == null;
        Color old = GUI.color;
        if (why != null) GUI.color = new Color(old.r, old.g, old.b, 0.55f);
        if (e.type == EventType.Repaint)
        {
            GUI.color = new Color(1f, 1f, 1f, why != null ? 0.03f : 0.06f);
            GUI.DrawTexture(row, Texture2D.whiteTexture);
            GUI.color = why != null ? new Color(old.r, old.g, old.b, 0.55f) : old;
        }
        float ic = row.height - 6f;
        DrawUnitIcon(new Rect(row.x + 2f, row.y + 3f, ic, ic), d.name);
        float bw1 = UiKit.Phone ? 46f : 44f;
        float tx = row.x + ic + 8f, tw = row.xMax - tx - bw1 * 2f - 10f;
        LocGUI.Label(new Rect(tx, row.y + 1f, tw, row.height / 2f), $"<b>{d.name}</b> " + UiKit.Col(UiKit.MutedHex, $"{d.hp:0} здор · {d.dmg:0} урон · {d.train * WarRules.TrainMul:0} с"), IconLabel);
        LocGUI.Label(new Rect(tx, row.y + row.height / 2f - 1f, tw, row.height / 2f), why != null ? UiKit.Col(UiKit.BadHex, why) : CostText(d.gold, food, d.wood, 0, d.iron) + UiKit.Col(UiKit.MutedHex, " · " + WarRules.UnitRole(d)), IconLabel);
        GUI.color = old;
        string tip = $"{d.name}: {WarRules.UnitRole(d)}.\nЗдоровье {d.hp:0}, урон {d.dmg:0}, дальность {d.range:0.#}, скорость {d.speed:0.#}, мест {d.space}." + (d.mounted ? $"\nКонь — ещё {WarRules.HorseFood} еды." : "") + (lack != null ? "\n" + lack : "") + (why != null ? "\n" + why : "");
        UiKit.Tip(new Rect(row.x, row.y, tw + ic + 8f, row.height), tip);
        float by = row.y + (row.height - 30f) / 2f;
        if (UiKit.Button(new Rect(row.xMax - bw1 * 2f - 4f, by, bw1, 30f), "+1", can, UiKit.Kind.Primary, why ?? lack ?? $"Нанять 1: {CostText(d.gold, food, d.wood, 0, d.iron)}")) Train(d, 1);
        if (UiKit.Button(new Rect(row.xMax - bw1, by, bw1, 30f), "+5", can, UiKit.Kind.Normal, why ?? lack ?? "Нанять 5 (сколько хватит ресурсов и мест)")) Train(d, 5);
    }

    void DrawResearchTab(Rect view)
    {
        float rowH = 74f;
        UiKit.Scroll("war.research", view, 40f + rowH * 2f + 10f, c =>
        {
            float y = c.y;
            LocGUI.Label(new Rect(c.x, y, c.width, 36f), UiKit.Col(UiKit.MutedHex, "Улучшения — на всю войну, для всех бойцов вашего города (и уже нанятых)."), SmallWrap);
            y += 40f;
            for (int k = 0; k < 2; k++)
            {
                int lv = k == WarRules.ResWeapon ? fr.weaponLv : fr.armorLv;
                Rect row = new Rect(c.x, y, c.width, rowH - 6f);
                if (e.type == EventType.Repaint) { Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.06f); GUI.DrawTexture(row, Texture2D.whiteTexture); GUI.color = o; }
                string what = k == WarRules.ResWeapon ? "урона" : "здоровья";
                LocGUI.Label(new Rect(row.x + 8f, row.y + 4f, row.width - 150f, 22f), $"<b>{WarRules.ResearchName(k)}</b> ур.{lv}/{WarRules.MaxResearch}" + UiKit.Col(UiKit.MutedHex, $" · сейчас +{Mathf.RoundToInt(lv * WarRules.ResearchStep * 100)}% {what}"), IconLabel);
                if (lv >= WarRules.MaxResearch)
                {
                    LocGUI.Label(new Rect(row.x + 8f, row.y + 28f, row.width - 16f, 22f), UiKit.Col(UiKit.GoodHex, "Лучше уже некуда"), OneSmall);
                }
                else
                {
                    WarRules.ResearchCost(k, lv + 1, out int g, out int wd, out int s, out int ir);
                    string lack = Lack(g, 0, wd, s, ir);
                    LocGUI.Label(new Rect(row.x + 8f, row.y + 28f, row.width - 150f, 22f), $"Ур.{lv + 1}: +{Mathf.RoundToInt(WarRules.ResearchStep * 100)}% {what} · " + CostText(g, 0, wd, s, ir), IconLabel);
                    int kind = k;
                    if (UiKit.Button(new Rect(row.xMax - 134f, row.y + (row.height - 32f) / 2f, 126f, 32f), "Улучшить", lack == null && !(fr.fallen), UiKit.Kind.Primary, lack ?? $"{WarRules.ResearchName(k)} ур.{lv + 1}"))
                        Send(new WarCommand { kind = WarCommand.Research, squad = kind });
                }
                y += rowH;
            }
        });
    }

    void DrawRepairTab(Rect view)
    {
        var list = new List<BldView>();
        foreach (var b in blds.Values) if (b.d.city == st.myCity && b.hp < 100) list.Add(b);
        list.Sort((a, b) => a.hp.CompareTo(b.hp));
        int tw = 0, ts = 0, can = 0;
        foreach (var b in list)
        {
            if (b.repairing || (b.d.type == WarRules.HallName && !b.Alive)) continue;
            WarRules.RepairCost(b.d.maxHp * (100 - b.hp) / 100f, !b.Alive, out int w, out int s);
            tw += w; ts += s; can++;
        }
        float rowH = 34f;
        UiKit.Scroll("war.repair", view, 76f + list.Count * rowH + 10f, c =>
        {
            float y = c.y;
            if (list.Count == 0)
            {
                LocGUI.Label(new Rect(c.x, y, c.width, 60f), UiKit.Col(UiKit.MutedHex, "Все здания целы. Сломанные враги здания здесь можно починить за дерево и камень."), SmallWrap);
                return;
            }
            LocGUI.Label(new Rect(c.x, y, c.width, 36f), UiKit.Col(UiKit.MutedHex, $"Повреждено зданий: {list.Count}. Чинят постепенно, пока рядом нет врагов ({WarRules.RepairSafeRange:0} клеток)."), SmallWrap);
            y += 38f;
            string lackAll = can == 0 ? "Всё уже чинят" : Lack(0, 0, tw, ts, 0);
            if (UiKit.Button(new Rect(c.x, y, c.width, 30f), can > 0 ? $"Чинить всё ({can}): " + CostText(0, 0, tw, ts, 0) : "Всё уже чинят", can > 0, UiKit.Kind.Primary,
                    lackAll ?? "Хватит не на всё — починятся те, на что хватит"))
                Send(new WarCommand { kind = WarCommand.Repair, building = -1 }, () => Say("Ремонт начался", false));
            y += 36f;
            foreach (var b in list)
            {
                Rect row = new Rect(c.x, y, c.width, rowH - 4f);
                string state = !b.Alive ? UiKit.Col(UiKit.BadHex, "разрушено") : $"{b.hp}%";
                bool hallDead = b.d.type == WarRules.HallName && !b.Alive;
                LocGUI.Label(new Rect(row.x, row.y, row.width - 150f, row.height), $"{b.d.type} · {state}" + (b.repairing ? UiKit.Col(UiKit.GoodHex, " · чинят") : hallDead ? UiKit.Col(UiKit.MutedHex, " · не восстановить") : ""), IconLabel);
                if (UiKit.Button(new Rect(row.xMax - 144f, row.y, 70f, row.height), "Где", UiKit.Kind.Ghost, "Показать на карте"))
                { cam.transform.position = new Vector3(b.Center.x, b.Center.y, -10f); selected.Clear(); selectedBld = b.d.id; }
                if (!b.repairing && !hallDead)
                {
                    WarRules.RepairCost(b.d.maxHp * (100 - b.hp) / 100f, !b.Alive, out int w, out int s);
                    string lack = Lack(0, 0, w, s, 0);
                    int id = b.d.id;
                    if (UiKit.Button(new Rect(row.xMax - 70f, row.y, 70f, row.height), "Чинить", lack == null, UiKit.Kind.Normal, lack ?? CostText(0, 0, w, s, 0)))
                        Send(new WarCommand { kind = WarCommand.Repair, building = id });
                }
                y += rowH;
            }
        });
    }

    // ---------- Миникарта ----------

    void DrawMini()
    {
        Rect r = MiniRect;
        if (r.width <= 0f) return;
        Rect frame = new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f);
        LocGUI.Box(frame, GUIContent.none);
        Ui(frame);
        if (e.type != EventType.Repaint) { UiKit.Tip(r, MiniTip); return; }
        if (miniTex != null) GUI.DrawTexture(r, miniTex);
        if (miniOwnerTex != null) GUI.DrawTexture(r, miniOwnerTex);
        Color old = GUI.color;
        float kx = r.width / W, ky = r.height / H;
        // Цитадели
        foreach (var b in blds.Values)
        {
            if (b.d.type != WarRules.HallName) continue;
            GUI.color = !b.Alive ? Color.gray : b.d.city == st.myCity ? new Color(0.6f, 1f, 0.6f) : b.side == st.mySide ? new Color(0.6f, 0.8f, 1f) : new Color(1f, 0.5f, 0.4f);
            float s = Mathf.Max(5f, b.d.w * kx);
            GUI.DrawTexture(new Rect(r.x + (b.d.x + b.d.w / 2f) * kx - s / 2f, r.yMax - (b.d.y + b.d.h / 2f) * ky - s / 2f, s, s), Texture2D.whiteTexture);
        }
        // final-13-fix-2: источники и бойцы - одной картинкой, она обновляется 5 раз в секунду.
        // Раньше каждый боец и источник был отдельным квадратиком (сотни отрисовок за кадр - на телефоне подтормаживало)
        if (Time.unscaledTime >= miniDotsAt) { miniDotsAt = Time.unscaledTime + 0.2f; RedrawMiniDots(); }
        if (miniDots != null) { GUI.color = Color.white; GUI.DrawTexture(r, miniDots); }
        // Что видно на экране
        float hh = cam.orthographicSize, hw = hh * cam.aspect;
        Vector2 cc = (Vector2)cam.transform.position - Origin;
        Rect v = new Rect(r.x + (cc.x - hw) * kx, r.yMax - (cc.y + hh) * ky, hw * 2f * kx, hh * 2f * ky);
        v = Rect.MinMaxRect(Mathf.Max(r.x, v.x), Mathf.Max(r.y, v.y), Mathf.Min(r.xMax, v.xMax), Mathf.Min(r.yMax, v.yMax));
        GUI.color = new Color(1f, 1f, 1f, 0.85f);
        GUI.DrawTexture(new Rect(v.x, v.y, v.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(v.x, v.yMax, v.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(v.x, v.y, 1f, v.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(v.xMax, v.y, 1f, v.height), Texture2D.whiteTexture);
        GUI.color = old;
        UiKit.Tip(r, MiniTip);
    }

    Texture2D miniDots;
    Color32[] miniDotsPix;
    float miniDotsAt;
    const int MiniDotsMax = 192;

    void RedrawMiniDots()
    {
        if (W <= 0 || H <= 0 || st == null) return;
        float k = MiniDotsMax / (float)Mathf.Max(W, H);
        int tw = Mathf.Max(8, Mathf.RoundToInt(W * k)), th = Mathf.Max(8, Mathf.RoundToInt(H * k));
        if (miniDots == null || miniDots.width != tw || miniDots.height != th)
        {
            if (miniDots != null) Destroy(miniDots);
            miniDots = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            miniDotsPix = new Color32[tw * th];
        }
        System.Array.Clear(miniDotsPix, 0, miniDotsPix.Length);
        float kx = tw / (float)W, ky = th / (float)H;
        foreach (var sv in srcs)
        {
            int cx = (int)((sv.d.x + WarRules.SourceSize / 2f) * kx), cy = (int)((sv.d.y + WarRules.SourceSize / 2f) * ky);
            Dot(cx, cy, 2, new Color32(26, 20, 10, 230));
            Dot(cx, cy, 1, OwnerColor(SourceOwner(sv.d)));
        }
        foreach (var u in units.Values)
        {
            bool sel = selected.Contains(u.id);
            Color c = sel ? new Color(0.6f, 1f, 0.6f) : u.side != st.mySide ? EnemyColor : u.mine ? new Color(0.7f, 0.9f, 1f) : AllyColor;
            Dot((int)(u.pos.x * kx), (int)(u.pos.y * ky), sel || u.mine ? 1 : 0, c);
        }
        miniDots.SetPixels32(miniDotsPix);
        miniDots.Apply(false);
    }

    // Квадратик (2r+1)x(2r+1) в картинке миникарты; r = 0 - точка 2x2
    void Dot(int cx, int cy, int r, Color32 c)
    {
        int tw = miniDots.width, th = miniDots.height;
        int x0 = cx - r, x1 = r == 0 ? cx + 1 : cx + r, y0 = cy - r, y1 = r == 0 ? cy + 1 : cy + r;
        for (int y = Mathf.Max(0, y0); y <= Mathf.Min(th - 1, y1); y++)
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(tw - 1, x1); x++)
                miniDotsPix[y * tw + x] = c;
    }

    static string MiniTip => UiKit.Phone
        ? "Карта войны: нажмите — перейти туда, долгое нажатие — послать выбранных бойцов.\nСиние — ваши, красные — враг, квадратики — источники ресурсов, зелёный — ваша Цитадель"
        : "Карта войны: ЛКМ — перейти, ПКМ — послать выбранных бойцов.\nСиние — ваши, красные — враг, квадратики — источники ресурсов, зелёный — ваша Цитадель";

    Vector2 MiniCell(Vector2 gp)
    {
        Rect r = MiniRect;
        return new Vector2((gp.x - r.x) / r.width * W, (r.yMax - gp.y) / r.height * H);
    }

    void MiniJump(Vector2 gp)
    {
        Vector2 c = MiniCell(gp);
        if (embedded) { if (CameraController.Instance != null) CameraController.Instance.FocusOn(Origin + c); return; }
        cam.transform.position = new Vector3(Origin.x + c.x, Origin.y + c.y, -10f);
    }

    void MiniOrder(Vector2 gp, bool plain)
    {
        MoveOrder(MiniCell(gp), plain);
    }

    // ---------- Подписи на карте ----------

    void DrawWorldLabels()
    {
        if (e.type != EventType.Repaint) return;
        float cell = UiKit.PointsPerUnit(cam);   // точек на клетку
        Color old = GUI.color;

        // Названия городов
        if (st.cities != null)
            foreach (var c in st.cities)
            {
                Vector2 top = Origin + new Vector2(WarRules.CityCenterX(c.plotX), c.plotY * WarRules.Plot + WarRules.CitySize - 4f);
                if (!OnScreen(top, 2f)) continue;
                Vector2 g = ToGui(top);
                string label = (c.side == st.mySide ? UiKit.Col(UiKit.InfoHex, Loc.Keep(c.name)) : UiKit.Col(UiKit.BadHex, Loc.Keep(c.name))) + (c.fallen ? UiKit.Col(UiKit.MutedHex, " (пал)") : "") + (c.idx == st.myCity ? UiKit.Col(UiKit.GoodHex, " — вы") : "");
                LocGUI.Label(new Rect(g.x - 150f, g.y - 12f, 300f, 24f), label, MidCenter);
            }

        // Источники: название (когда карта не слишком далеко)
        if (cell >= 2.5f)
            foreach (var v in srcs)
            {
                Vector2 top = v.Center + new Vector2(0f, WarRules.SourceSize / 2f + 0.8f);
                if (!OnScreen(top, 2f)) continue;
                Vector2 g = ToGui(top);
                int o = SourceOwner(v.d);
                string name = WarRules.SourceName(v.d.kind);
                string label = o == 0 ? UiKit.Col(UiKit.AccentHex, name) : o == st.mySide ? UiKit.Col(UiKit.InfoHex, name) : UiKit.Col(UiKit.BadHex, name);
                LocGUI.Label(new Rect(g.x - 100f, g.y - 11f, 200f, 22f), label, MidCenter);
            }

        // Захват участков
        foreach (string s in plotCapture)
        {
            string[] a = s.Split(',');
            if (a.Length < 3 || !int.TryParse(a[0], out int i) || !int.TryParse(a[1], out int side) || !int.TryParse(a[2], out int pct)) continue;
            int px = i % G.Cols, py = i / G.Cols;
            Vector2 mid = Origin + new Vector2(px * WarRules.Plot + WarRules.Plot / 2f, py * WarRules.Plot + WarRules.Plot / 2f);
            if (!OnScreen(mid, 2f)) continue;
            Vector2 g = ToGui(mid);
            UiKit.Bar(new Rect(g.x - 80f, g.y - 9f, 160f, 18f), pct / 100f, side == st.mySide ? UiKit.Info : UiKit.Bad, $"Захват {pct}%");
        }

        if (cell < 6f) { GUI.color = old; return; }

        // Здоровье зданий
        foreach (var b in blds.Values)
        {
            if (b.hp >= 100 || !b.Alive || !OnScreen(b.Center, 2f)) continue;
            Vector2 g = ToGui(Origin + new Vector2(b.d.x + b.d.w / 2f, b.d.y + b.d.h));
            float w = b.d.w * cell;
            Bar(new Rect(g.x - w / 2f, g.y - 6f, w, 4f), b.hp / 100f, b.repairing ? UiKit.Info : b.side == st.mySide ? UiKit.Good : UiKit.Bad);
        }
        // Здоровье бойцов и номер отряда у своих
        bool digits = cell >= 14f;
        foreach (var u in units.Values)
        {
            bool sel = selected.Contains(u.id);
            if (!OnScreen(u.World, 1f)) continue;
            Vector2 g = ToGui(u.World + new Vector2(0f, 0.5f));
            float w = Mathf.Clamp(cell * 0.8f, 10f, 30f);
            if (u.hp < 100 || sel) Bar(new Rect(g.x - w / 2f, g.y - 5f, w, 3f), u.hp / 100f, u.side != st.mySide ? UiKit.Bad : UiKit.Good);
            if (digits && u.mine && u.squad > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.9f);
                LocGUI.Label(new Rect(g.x + w / 2f - 2f, g.y - 16f, 18f, 16f), UiKit.Col(sel ? UiKit.GoodHex : UiKit.InfoHex, u.squad.ToString()), UiKit.Small);
                GUI.color = old;
            }
        }
        GUI.color = old;
    }

    static void Bar(Rect r, float k, Color fill)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), Texture2D.whiteTexture);
    }

    void DrawBox()
    {
        Mouse m = Mouse.current;
        if (!leftDown || m == null || (UiKit.Phone && !TouchControls.BoxSelect)) return;   // final-13-fix-2: на телефоне рамка - только с кнопкой «Рамка»
        Vector2 a = leftStart, b = m.position.ReadValue();
        if ((b - a).sqrMagnitude <= 64f) return;
        Vector2 ga = UiKit.ToGui(a), gb = UiKit.ToGui(b);
        Rect r = Rect.MinMaxRect(Mathf.Min(ga.x, gb.x), Mathf.Min(ga.y, gb.y), Mathf.Max(ga.x, gb.x), Mathf.Max(ga.y, gb.y));
        Color old = GUI.color;
        GUI.color = new Color(0.5f, 1f, 0.5f, 0.15f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.5f, 1f, 0.5f, 0.8f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax, r.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax, r.y, 1f, r.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    // ---------- Сообщение посередине сверху ----------

    string BannerText()
    {
        if (st == null) return string.IsNullOrEmpty(status) ? UiKit.Col(UiKit.MutedHex, "Загрузка карты войны…") : UiKit.Col(UiKit.WarnHex, status);
        if (building != null) return UiKit.Col(UiKit.MutedHex, building);
        if (DefenseWindow.UnderRaid && !embedded) return UiKit.Col(UiKit.BadHex, "На вашу деревню напали! Нажмите «Выйти», чтобы защищаться");
        if (fr != null && (fr.winner != 0 || fr.stage >= (int)WarStage.Finished))
        {
            if (fr.winner == 0) return UiKit.Col(UiKit.AccentHex, "Война окончена: ничья");
            return fr.winner == st.mySide ? UiKit.Col(UiKit.GoodHex, "Победа! Клан получил рейтинг") : UiKit.Col(UiKit.BadHex, "Война проиграна");
        }
        if (!string.IsNullOrEmpty(status)) return UiKit.Col(UiKit.WarnHex, status);
        if (fr != null && fr.fallen) return UiKit.Col(UiKit.BadHex, embedded ? "Враги снесли Цитадель: деревня пала. Уцелевшие бойцы ещё могут сражаться" : "Ваш город пал. Уцелевшие бойцы ещё могут сражаться");
        return null;
    }

    void DrawBanner()
    {
        string t = BannerText();
        if (t == null) return;
        float w = Mathf.Min(520f, UiKit.W - 20f);
        float x0 = hireOpen && st != null ? HireRect.xMax + 6f : 0f;
        float x1 = MiniRect.width > 0f ? MiniRect.x - 6f : UiKit.W;
        if (embedded) { x0 = 0f; x1 = ChipRect.x - 6f; }
        w = Mathf.Min(w, Mathf.Max(220f, x1 - x0 - 12f));
        float by = embedded ? ChipRect.y + (UiKit.Phone ? 60f : 80f) : st == null || building != null ? UiKit.H / 2f - 22f : TopH + 8f + (UiKit.Phone ? 80f : 120f);
        Rect r = new Rect((x0 + x1) / 2f - w / 2f, by, w, 44f);
        LocGUI.Box(r, GUIContent.none);
        LocGUI.Label(new Rect(r.x + 10f, r.y, r.width - 20f, r.height), t, WrapCenter);
    }

    // ---------- Как играть ----------

    void CloseHelp()
    {
        helpOpen = false;
        PlayerPrefs.SetInt(embedded ? LiveHelpKey : HelpKey, 1);
    }

    const string LiveHelpText =
        "<b>Ваша деревня на войне живёт.</b> Это копия домашней: жители добывают и носят, мастерские делают доски, кузни — оружие, конюшни растят лошадей, казармы учат бойцов. Строить, улучшать и сносить нельзя, ресурсы свои: стартовый запас и то, что добудете на войне. С домом деревня не связана — домой возвращается только рейтинг клана.\n\n" +
        "<b>Общая карта.</b> Отдалите камеру («Вся карта» или двумя пальцами): вокруг — деревни соседей по клану, враги и источники ресурсов. Справа сверху — счёт и этап войны, нажмите на него — миникарта и события.\n\n" +
        "<b>Армия.</b> Казармы учат бойцов, как дома. Обученные сами выходят из казарм на карту и встают в отряды по видам войск: пехота, стрелки, конница, маги, осада. Хотите оставить бойцов дома (например, продать на рынке клана) — в казарме включите «Держать в казарме».\n\n" +
        "<b>Отряды и приказы.</b> Внизу — кнопки отрядов: нажмите, чтобы выбрать весь отряд, ещё раз — показать его. Выбрали — нажмите по земле, и бойцы пойдут туда с боем; по врагу или его зданию — нападут. «Просто идти» — не отвлекаться на врагов, «Стоять» — держать место. «В отряд…» — собрать свой отряд или переименовать.\n\n" +
        "<b>Цель.</b> Счёт — занятые участки карты и по 2 очка за каждую целую деревню. Снесёте вражескую Цитадель — деревня падёт. Через 7 дней побеждает клан с большим счётом. День 2 — ходят только по своей земле, дни 3–4 — открыты нейтральные земли с источниками, дни 5–7 — вся карта.\n\n" +
        "<b>Ресурсы с карты.</b> Займите участок с источником (деревня, лесопилка, каменоломня, рудник) — и его доход приходит в вашу деревню, пока участок ваш.\n\n" +
        "<b>Помогать своим.</b> «Клану»: подарите ресурсы соседу — особенно деревням у края, им первыми достаётся от врага. Там же рынок клана: продайте лишнее, купите нужное.\n\n" +
        "<b>Разрушения.</b> Враги снесли здание на карте — в деревне оно не работает и само не встаёт. Выберите его в деревне и нажмите «Починить» (доски и камень деревни).\n\n" +
        "<b>Управление.</b> Телефон: нажать на своего бойца — выбрать (дважды — всех такого вида), долгое нажатие по земле — «просто идти», «Рамка» — выбрать пальцем многих. Компьютер: ЛКМ — выбрать (рамкой — многих), ЛКМ или ПКМ по земле — приказ, Ctrl — просто идти, S — стоп, H — стоять, 1–6 — отряд, Ctrl+1–6 — в отряд, Пробел — к деревне.";

    const string HelpText =
        "<b>Только смотреть.</b> Вашей деревни на этой войне нет (или она ещё не загружена): здесь видно всю карту, счёт и бои. Чтобы воевать, войдите в войну из окна клана, когда ваша деревня в схеме клана.\n\n" +
        "<b>Цель.</b> Счёт — занятые участки карты и по 2 очка за каждый целый город. Снесёте вражескую Цитадель — город падёт. Через 7 дней побеждает клан с большим счётом, его рейтинг растёт.\n\n" +
        "<b>Этапы.</b> День 1 — кланы расставляют города. День 2 — бойцы ходят только по своей земле. Дни 3–4 открыты нейтральные земли с источниками ресурсов. Дни 5–7 открыта вся карта.\n\n" +
        "<b>Отряды.</b> Внизу — кнопки отрядов: нажмите, чтобы выбрать весь отряд, ещё раз — показать его на карте. «Все» — все бойцы. Выберите бойцов и нажмите «В отряд…», чтобы собрать свой отряд (1–6) или переименовать его.\n\n" +
        "<b>Приказы.</b> Выбрали бойцов — нажмите по земле, и они пойдут туда, сражаясь по пути («Идти с боем»). Переключите на «Просто идти», чтобы не отвлекались. Нажмите по врагу или его зданию — нападут. «Стоять» — держать место. «Стоп» — остановиться.\n\n" +
        "<b>Управление на телефоне.</b> Нажать на своего бойца — выбрать (дважды — всех такого вида на экране). Вести пальцем — двигать карту, двумя пальцами — приблизить. «Рамка» — выбрать пальцем сразу многих. Долгое нажатие по земле — «просто идти». Миникарта справа сверху: нажмите, чтобы перейти.\n\n" +
        "<b>На компьютере.</b> ЛКМ — выбрать (рамкой — многих, Shift — добавить), ЛКМ или ПКМ по земле — приказ, Ctrl — просто идти. Колесо — приблизить, тянуть ПКМ — двигать карту. S — стоп, H — стоять, 1–6 — отряд, Ctrl+1–6 — в отряд, Ctrl+A — все, Пробел — к своему городу, Esc — снять выбор или выйти.";

    void DrawHelp()
    {
        float w = Mathf.Min(680f, UiKit.W - 16f), h = Mathf.Min(UiKit.H - 16f, 560f);
        Rect r = new Rect((UiKit.W - w) / 2f, (UiKit.H - h) / 2f, w, h);
        Rect c = UiKit.Window(r, "Клановая война: как играть", out bool close);
        Ui(r);
        if (close) { CloseHelp(); return; }
        float btnH = 34f;
        Rect view = new Rect(c.x, c.y, c.width, c.height - btnH - 8f);
        string text = embedded ? LiveHelpText : HelpText;
        float th = UiKit.TextH(text, view.width - UiKit.ScrollBarW - 4f, SmallWrap) + 10f;
        UiKit.Scroll("war.help", view, th, cc => LocGUI.Label(new Rect(cc.x, cc.y, cc.width, th), text, SmallWrap));
        if (UiKit.Button(new Rect(c.x + c.width / 2f - 90f, c.yMax - btnH, 180f, btnH), "Понятно", UiKit.Kind.Primary, null)) CloseHelp();
    }
}
