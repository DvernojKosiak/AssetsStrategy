using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Кнопки на экране для телефона - вместо клавиш. Появляются только там, где без клавиатуры не обойтись:
///  - в руках здание, дозор, ловушка или переправа - панель внизу по центру:
///    «✕ Отмена» (Esc), «↻ Повернуть» (R), «Подряд» (Shift), «Линией» (дороги и стены), «✓ Поставить»;
///  - карта войны - столбик справа: «Рамка», «Все бойцы» (Ctrl+A), «Стоп» (S), «К городу» (пробел);
/// «Назад» - системная кнопка телефона (она же Esc). Подсказка по жестам - в настройках (⚙ → «Управление пальцами»).
/// Долгое нажатие на здание открывает меню «Перенести» (M) / «Снести» (X).
///
/// Ещё здесь: пальцем листаются списки в окнах (движение пальца становится прокруткой колесом),
/// долгое нажатие на кнопку показывает подсказку и не нажимает кнопку,
/// и учитывается, где на экране окна (LocGUI.Box сообщает сюда свои прямоугольники),
/// чтобы палец на окне не двигал карту.
/// Все размеры - в точках окон (UiKit): на телефоне это примерно точки Android, кнопка 44 точки ≈ 7 мм.
/// </summary>
public class TouchControls : MonoBehaviour
{
    public static TouchControls Instance { get; private set; }

    /// <summary>Включено «Ещё» / «Добавлять» - держится Shift.</summary>
    public static bool Keep { get; private set; }
    /// <summary>Включено «Линией»: стены и дороги рисуются, пока палец ведут по карте.</summary>
    public static bool Paint { get; private set; }
    /// <summary>Карта войны: палец рисует рамку выбора вместо того, чтобы двигать карту.</summary>
    public static bool BoxSelect { get; set; }   // карта войны: кнопка «Рамка» в полосе отрядов (WarView)

    // Размеры кнопок, точки окон
    const float BtnH = 44f, Gap = 6f;

    // Меню здания (долгое нажатие)
    bool menuOpen;
    Vector2 menuScreen;   // точка касания, координаты Input System
    Rect menuRect;

    bool settingsOpen, helpOpen;
    Rect settingsRect;

    readonly List<Rect> barRects = new List<Rect>();

    // Окна, нарисованные в прошлом кадре (точки окон)
    static readonly List<Rect> boxes = new List<Rect>(), boxesNext = new List<Rect>();
    static int boxesFrame = -1;

    bool guiScrollGrabbed, tipCancelled;
    int scrollFrame = -1;
    GUIStyle btn, btnOn, btnPrimary, btnDanger;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Keep = Paint = BoxSelect = false; boxes.Clear(); boxesNext.Clear(); boxesFrame = -1; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ---------- Где интерфейс ----------

    /// <summary>LocGUI.Box вызывает это для каждого окна и плашки (только когда включено сенсорное управление).</summary>
    public static void NoteBox(Rect r)
    {
        if (!TouchInput.Active || Event.current == null || Event.current.type != EventType.Repaint) return;
        if (r.width * r.height < 1600f) return;   // мелкие значки над картой не мешают двигать карту
        if (boxesFrame != Time.frameCount) { boxes.Clear(); boxes.AddRange(boxesNext); boxesNext.Clear(); boxesFrame = Time.frameCount; }
        Rect top = UiKit.LocalToTop(r);   // окно может быть внутри группы или прокрутки
        if (clipOn)   // внутри прокрутки видна только её рамка: невидимая часть окна не должна «закрывать» карту (final-6)
        {
            top = Rect.MinMaxRect(Mathf.Max(top.xMin, clip.xMin), Mathf.Max(top.yMin, clip.yMin), Mathf.Min(top.xMax, clip.xMax), Mathf.Min(top.yMax, clip.yMax));
            if (top.width <= 0f || top.height <= 0f) return;
        }
        boxesNext.Add(top);
    }

    static Rect clip;
    static bool clipOn;
    /// <summary>Окна дальше рисуются внутри прокрутки с этой рамкой (точки окон). ClipEnd - конец.</summary>
    public static void ClipBegin(Rect topRect) { clip = topRect; clipOn = true; }
    public static void ClipEnd() { clipOn = false; }


    /// <summary>Точка (точки окон, y сверху) занята интерфейсом: кнопкой, окном, нашими кнопками.</summary>
    public static bool UiAt(Vector2 gp, bool ghost)
    {
        if (GUIUtility.hotControl != 0) return true;           // палец держит кнопку, ползунок, полосу прокрутки
        if (TouchGuiProbe.ConsumedRecently) return true;       // касание забрал какой-то элемент интерфейса
        return UiCovers(gp);
    }

    /// <summary>Точка закрыта окном или кнопкой (без учёта того, что палец сейчас держит). Метки над картой тут не рисуют.</summary>
    public static bool UiCovers(Vector2 gp)
    {
        var me = Instance;
        if (me != null)
        {
            foreach (var r in me.barRects) if (r.Contains(gp)) return true;
            if (me.menuOpen && me.menuRect.Contains(gp)) return true;
            if (me.settingsOpen && me.settingsRect.Contains(gp)) return true;
        }
        if (boxesFrame >= Time.frameCount - 2)
        {
            foreach (var r in boxes) if (r.Contains(gp)) return true;
            foreach (var r in boxesNext) if (r.Contains(gp)) return true;
        }
        try
        {
            if (RaidManager.IsPointerOverUI(gp) || DevelopmentPath.IsPointerOver(gp) || WeaponForge.IsPointerOver(gp)
                || MarketWindow.IsPointerOver(gp) || ClanWindow.IsPointerOver(gp) || DefenseWindow.IsPointerOver(gp)
                || AccountManager.IsPointerOver(gp)) return true;
            if (GameMenu.Instance != null && GameMenu.Instance.IsPointerOver(gp)) return true;
            if (BuildingPlacer.IsPointerOverPanel(gp)) return true;   // окно здания: палец листает его, а не карту
            if (GameClock.Instance != null && GameClock.Instance.IsPointerOver(gp)) return true;
        }
        catch (System.Exception) { }
        return false;
    }

    /// <summary>Метка над картой (полоска, надпись) залезла бы на окно или кнопку.</summary>
    public static bool UiCovers(Rect r)
    {
        if (!TouchInput.Active) return false;
        float y = r.center.y;
        return UiCovers(new Vector2(r.x, y)) || UiCovers(r.center) || UiCovers(new Vector2(r.xMax, y));
    }

    // ---------- Меню здания ----------

    int menuActs = 3;   // 1 - «Перенести», 2 - «Снести»

    public static void OpenMenu(Vector2 screen, int acts = 3)
    {
        var me = Instance;
        if (me == null || acts == 0) return;
        me.menuOpen = true;
        me.menuScreen = screen;
        me.menuActs = acts;
    }

    public static void CloseMenu() { if (Instance != null) Instance.menuOpen = false; }

    /// <summary>Открыть подсказку «Как управлять пальцами».</summary>
    public static void ShowHelp() { if (Instance != null) { Instance.settingsOpen = true; Instance.helpOpen = true; } }

    /// <summary>Новое касание (точки окон). true - касание закрыло меню и дальше не идёт.</summary>
    public static bool TouchBegan(Vector2 gp)
    {
        var me = Instance;
        if (me == null) return false;
        if (me.menuOpen && !me.menuRect.Contains(gp)) { me.menuOpen = false; return true; }
        return false;
    }

    // ---------- Кадр ----------

    void Update()
    {
        if (!TouchInput.Active) { menuOpen = settingsOpen = false; return; }
        if (!TouchInput.Ghost) Paint = false;
        if (!TouchInput.WarMap) BoxSelect = false;
        if (!TouchInput.Ghost && !RaidManager.ViewingBattle) Keep = false;
        if (TouchInput.Ghost || TouchInput.WarMap || RaidManager.ViewingBattle) menuOpen = false;
    }

    void OnGUI()
    {
        GUI.depth = -3000;   // поверх всех окон и первыми получаем касания
        if (!TouchInput.Active) { barRects.Clear(); return; }

        Event e = Event.current;
        if (e.type == EventType.MouseDown) { TouchGuiProbe.MarkDown(); tipCancelled = false; }

        // Палец листает окно: список сдвигает UiKit.BeginScrollView (каждый кадр, ровно за пальцем).
        // Здесь только гасим перетаскивание мышью, чтобы кнопка или ползунок под пальцем не сработали
        if (TouchInput.GuiScrolling && !TouchInput.ScoutMapOpen)
        {
            if (e.type == EventType.MouseDrag)
            {
                if (!guiScrollGrabbed) { GUIUtility.hotControl = 0; guiScrollGrabbed = true; }   // кнопка под пальцем не нажмётся
                e.type = EventType.Ignore;
            }
        }
        else guiScrollGrabbed = false;

        // Палец держат на кнопке - видна подсказка; отпустят - кнопка не нажмётся (иначе подсказку не прочитать)
        if (TouchInput.HoldingOnUi && !tipCancelled && GUIUtility.hotControl != 0 && UiKit.TipShownRecently)
        {
            GUIUtility.hotControl = 0;
            tipCancelled = true;
        }

        // Пальца нет - убираем «наведение», иначе подсказка последней нажатой кнопки висит на экране
        if (e.type == EventType.Repaint && !TouchInput.FingerDown && !menuOpen) e.mousePosition = new Vector2(-10000f, -10000f);

        UiKit.Apply();
        BuildStyles();
        DrawBar();
        if (menuOpen) DrawMenu();
        if (settingsOpen) DrawSettings();
        else if (!TouchSettings.HelpSeen && TouchSettings.IsPhone && !AccountManager.Blocking) { settingsOpen = true; helpOpen = true; TouchSettings.HelpSeen = true; }
    }

    // ---------- Стиль ----------

    void BuildStyles()
    {
        if (btn != null) return;
        btn = new GUIStyle(UiKit.ButtonStyle(UiKit.Kind.Normal)) { fontSize = 14, wordWrap = false };
        btnOn = new GUIStyle(UiKit.ButtonStyle(UiKit.Kind.Primary)) { fontSize = 14, wordWrap = false };
        btnPrimary = new GUIStyle(UiKit.ButtonStyle(UiKit.Kind.Primary)) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false };
        btnDanger = new GUIStyle(UiKit.ButtonStyle(UiKit.Kind.Danger)) { fontSize = 14, wordWrap = false };
    }

    // ---------- Кнопки вместо клавиш ----------

    struct Item { public string text; public GUIStyle st; public System.Action act; public float w; }
    readonly List<Item> items = new List<Item>();

    void Add(string text, GUIStyle st, System.Action act, float w = 0f) { items.Add(new Item { text = text, st = st, act = act, w = w }); }

    void DrawBar()
    {
        items.Clear();
        barRects.Clear();
        var ti = TouchInput.Instance;
        if (ti == null || AccountManager.Blocking) return;   // заставка, вход, нет связи - кнопки игры не нужны

        if (TouchInput.Ghost)
        {
            // Стройка: панель внизу по центру, над часами. Главная кнопка - справа, под большой палец
            Add("✕ Отмена", btnDanger, () => ti.PressKeys(Key.Escape), 104f);
            if (FieldPostManager.Placing || TouchInput.CrossingPlacing) Add("↻ Повернуть", btn, () => ti.PressKeys(Key.R), 116f);
            if (!BuildingPlacer.MovingNow)   // перенос - одно здание: «Подряд» и «Линией» ни к чему
            {
                // «Подряд»: после постановки здание остаётся в руках - можно поставить ещё одно такое же.
                // «Линией»: только дороги, стены и мосты - ведёте пальцем, и они ставятся по пути
                if (BuildingPlacer.PlacingPaintType) Add(Paint ? "Линией: да" : "Линией: нет", Paint ? btnOn : btn, () => Paint = !Paint, 116f);
                else
                {
                    Paint = false;
                    Add(Keep ? "Подряд: да" : "Подряд: нет", Keep ? btnOn : btn, () => Keep = !Keep, 110f);
                }
            }
            Add("✓ Поставить", btnPrimary, () => ti.ClickAtCursor(), 140f);
            DrawRow();
            return;
        }

        // Карта войны (klan-voina-1): свои кнопки внизу экрана (WarView) - столбик справа больше не закрывает миникарту и окна
        if (TouchInput.WarMap) return;

        // Набег: отряды выбираются в панели боя слева («Отряды»), отдельная кнопка «Добавлять» не нужна
        if (RaidManager.ViewingBattle) Keep = false;
    }

    // Ряд кнопок внизу по центру - над нижним рядом кнопок («В бой!», «Переправа», разведчики)
    void DrawRow()
    {
        float total = -Gap;
        foreach (var it in items) total += it.w + Gap;
        float x = Mathf.Max(6f, (UiKit.W - total) * 0.5f);
        float y = UiKit.H - 56f - BtnH;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            Rect r = new Rect(x, y, it.w, BtnH);
            barRects.Add(r);
            if (LocGUI.Button(r, it.text, it.st)) it.act();
            x += it.w + Gap;
        }
    }

    // Столбик кнопок у правого края
    void DrawColumn(float top)
    {
        const float w = 120f;
        float x = UiKit.W - w - 8f, y = top;
        foreach (var it in items)
        {
            Rect r = new Rect(x, y, w, BtnH - 4f);
            barRects.Add(r);
            if (LocGUI.Button(r, it.text, it.st)) it.act();
            y += BtnH - 4f + Gap;
        }
    }

    // ---------- Меню здания ----------

    void DrawMenu()
    {
        const float bw = 160f, bh = BtnH;
        int rows = 1 + ((menuActs & 1) != 0 ? 1 : 0) + ((menuActs & 2) != 0 ? 1 : 0);
        float h = bh * rows + Gap * (rows + 1);
        Vector2 gp = UiKit.ToGui(menuScreen);
        // Меню над пальцем, чтобы палец его не закрывал
        float x = Mathf.Clamp(gp.x - bw * 0.5f - Gap, 4f, UiKit.W - bw - Gap * 2f - 4f);
        float y = gp.y - h - 30f;
        if (y < 4f) y = Mathf.Min(gp.y + 30f, UiKit.H - h - 4f);
        menuRect = new Rect(x, y, bw + Gap * 2f, h);

        LocGUI.Box(menuRect, GUIContent.none);
        var ti = TouchInput.Instance;
        Rect r = new Rect(x + Gap, y + Gap, bw, bh);
        if ((menuActs & 1) != 0)
        {
            if (LocGUI.Button(r, "✥ Перенести", btn)) { ti.KeyAt(menuScreen, Key.M, true); menuOpen = false; }
            r.y += bh + Gap;
        }
        if ((menuActs & 2) != 0)
        {
            if (LocGUI.Button(r, "✖ Снести", btnDanger)) { ti.KeyAt(menuScreen, Key.X, false); menuOpen = false; }
            r.y += bh + Gap;
        }
        if (LocGUI.Button(r, "Закрыть", btn)) menuOpen = false;
    }

    // ---------- Настройки и подсказка ----------

    static readonly string[] Help =
    {
        "Нажать — выбрать здание, жителя, отряд или нажать кнопку.",
        "Провести пальцем по карте — двигать карту.",
        "Два пальца: развести или свести — приблизить или отдалить, вести — двигать карту.",
        "Перенести или снести здание: нажмите на него — кнопки «Перенести» и «Снести» под его окном. Или удерживайте палец на здании.",
        "Удерживать палец на кнопке — подсказка к ней (кнопка не нажмётся).",
        "Здание в руках: ведите пальцем — здание едет следом. «Поставить» или двойное нажатие — поставить. «Подряд» — после постройки взять такое же. «Линией» (дороги, стены, мосты) — вести пальцем, и они ставятся по пути.",
        "Карта войны: нажать — выбрать бойца, удерживать — приказ идти туда. «Рамка» — выбрать нескольких.",
        "Списки в окнах листаются пальцем вверх и вниз.",
        "Кнопка «Назад» телефона закрывает окно или отменяет стройку.",
        "Окна мелкие или крупные — ⚙ → «Размер интерфейса».",
    };

    void DrawSettings()
    {
        float w = Mathf.Min(UiKit.W - 20f, 560f);
        const float rowH = 40f;
        float textW = w - UiKit.Pad * 2f - UiKit.ScrollBarW;
        var helpStyle = new GUIStyle(UiKit.Body) { fontSize = 14 };
        float helpH = 0f;
        if (helpOpen) foreach (var s in Help) helpH += helpStyle.CalcHeight(Loc.Content(s), textW) + 4f;

        bool phone = TouchSettings.IsPhone;
        float contentH = UiKit.SectionH + rowH + 8f + rowH + 8f + helpH;
        float h = Mathf.Min(UiKit.H - 12f, UiKit.WindowH(contentH));
        settingsRect = new Rect((UiKit.W - w) * 0.5f, (UiKit.H - h) * 0.5f, w, h);

        Rect c = UiKit.Window(settingsRect, "Управление пальцами", out bool close);
        if (close) { settingsOpen = false; return; }
        UiKit.Scroll("touch-help", c, contentH, v =>
        {
            float x = v.x, y = v.y, cw = v.width;
            y = UiKit.Section(x, y, cw, "Сенсорное управление");
            string[] modes = { "Авто", "Вкл", "Выкл" };
            if (!phone)
            {
                int cur = (int)TouchSettings.TouchMode;
                int pick = UiKit.Tabs(new Rect(x, y, cw, rowH), modes, cur);
                if (pick != cur) TouchSettings.TouchMode = (TouchSettings.Mode)pick;
            }
            else LocGUI.Label(new Rect(x, y, cw, rowH), UiKit.Col(UiKit.MutedHex, "На телефоне включено всегда"), UiKit.Body);
            y += rowH + 8f;

            if (LocGUI.Button(new Rect(x, y, cw, rowH), helpOpen ? "▾ Как управлять" : "▸ Как управлять", btn)) helpOpen = !helpOpen;
            y += rowH + 8f;
            if (helpOpen)
                foreach (var s in Help)
                {
                    float hh = helpStyle.CalcHeight(Loc.Content(s), cw);
                    LocGUI.Label(new Rect(x, y, cw, hh), "•  " + Loc.T(s), helpStyle);
                    y += hh + 4f;
                }
        });
    }
}

/// <summary>
/// Последним получает события интерфейса: если нажатие до него не дошло или дошло «использованным»,
/// значит палец попал в кнопку или поле - тогда щелчок в карту не отправляется.
/// </summary>
public class TouchGuiProbe : MonoBehaviour
{
    static int downFrame = -10, reachedFrame = -10, consumedFrame = -10;

    public static void MarkDown() { downFrame = Time.frameCount; }

    /// <summary>Недавнее нажатие забрал элемент интерфейса.</summary>
    public static bool ConsumedRecently => Time.frameCount - consumedFrame <= 3;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { downFrame = reachedFrame = consumedFrame = -10; }

    void LateUpdate()
    {
        // Нажатие было, а сюда не дошло - его забрали раньше
        if (downFrame == Time.frameCount - 1 && reachedFrame != downFrame) consumedFrame = downFrame;
    }

    void OnGUI()
    {
        GUI.depth = 3000;   // самый нижний слой - события сюда приходят последними
        if (!TouchInput.Active) return;
        Event e = Event.current;
        if (e.type == EventType.MouseDown) reachedFrame = Time.frameCount;
        else if (e.type == EventType.Used && downFrame == Time.frameCount) { reachedFrame = Time.frameCount; consumedFrame = Time.frameCount; }
    }
}
