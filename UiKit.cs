using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Общий стиль интерфейса (IMGUI): тёмные окна с рамкой, плоские кнопки, золотые заголовки,
/// вкладки, полоски, сворачиваемые блоки «Подробнее» и всплывающие подсказки.
///
/// Как пользоваться:
///  - В начале каждого OnGUI: UiKit.Apply();  - все GUI.Box / Button / Label берут этот стиль.
///  - Окно: Rect c = UiKit.Window(rect, "Заголовок", out bool close); if (close) ...
///  - Подзаголовок раздела: y = UiKit.Section(x, y, w, "Склад");
///  - Кнопки: UiKit.Button(r, "Текст", UiKit.Kind.Primary / Danger / Normal, "подсказка");
///  - Вкладки: tab = UiKit.Tabs(r, new[] {"Обзор","Склад"}, tab);
///  - Полоска: UiKit.Bar(r, 0.4f, UiKit.Good, "40%");
///  - Сворачиваемый блок: if (UiKit.Fold(r, "id", "Подробнее")) { ... } (высота блока - FoldH)
///  - Подсказка при наведении: UiKit.Tip(r, "длинное пояснение");
///  - Цвета в тексте: UiKit.Col(UiKit.GoodHex, "ок"), UiKit.MutedHex и т.п.
/// Текстуры кнопок и окон рисуются программно; свои картинки можно подставить позже в BuildSkin.
/// </summary>
public static class UiKit
{
    // ---------- Палитра ----------
    public static readonly Color Panel = new Color(0.09f, 0.10f, 0.12f, 0.95f);
    public static readonly Color PanelEdge = new Color(0.58f, 0.47f, 0.29f, 1f);   // final-13: бронза-золото (было серое)
    public static readonly Color Text = new Color(0.92f, 0.91f, 0.88f);
    public static readonly Color Muted = new Color(0.62f, 0.63f, 0.66f);
    public static readonly Color Accent = new Color(0.96f, 0.80f, 0.42f);
    public static readonly Color Good = new Color(0.50f, 0.88f, 0.50f);
    public static readonly Color Bad = new Color(1f, 0.45f, 0.40f);
    public static readonly Color Warn = new Color(1f, 0.74f, 0.36f);
    public static readonly Color Info = new Color(0.55f, 0.80f, 1f);

    public const string AccentHex = "#F5CC6B";
    public const string GoodHex = "#80E080";
    public const string BadHex = "#FF7366";
    public const string WarnHex = "#FFBD5C";
    public const string InfoHex = "#8CCCFF";
    public const string MutedHex = "#9EA1A8";

    // Цвета ресурсов (для строк цены)
    public const string GoldHex = "#FFD54A";
    public const string WoodHex = "#D08A4E";
    public const string StoneHex = "#A9C6D6";
    public const string FoodHex = "#8FE07A";

    public static string Col(string hex, string s) { return $"<color={hex}>{s}</color>"; }
    public static string B(string s) { return $"<b>{s}</b>"; }

    /// <summary>Цена в одну строку: «120 зол · 60 дер · 40 кам». Нули пропускаются.</summary>
    public static string Cost(int gold, int wood, int stone, int food)
    {
        var parts = new List<string>();
        if (gold > 0) parts.Add(Col(GoldHex, gold + " зол"));
        if (wood > 0) parts.Add(Col(WoodHex, wood + " дер"));
        if (stone > 0) parts.Add(Col(StoneHex, stone + " кам"));
        if (food > 0) parts.Add(Col(FoodHex, food + " еды"));
        return parts.Count == 0 ? Col(MutedHex, "бесплатно") : string.Join(Col(MutedHex, " · "), parts);
    }

    // ---------- Размеры ----------
    public const float TitleH = 30f;     // шапка окна
    public const float Pad = 10f;        // отступ содержимого от края окна
    public const float RowH = 22f;       // строка текста
    public const float BtnH = 28f;       // обычная кнопка
    public const float SectionH = 24f;   // подзаголовок раздела
    public const float FoldH = 22f;      // строка «▸ Подробнее»

    // ---------- Стиль ----------
    static GUISkin skin;
    static GUIStyle title, section, body, small, muted, center, btnPrimary, btnDanger, btnGhost, tab, tabOn, fold, tipStyle, close, tipPhone;
    static Texture2D white;

    public enum Kind { Normal, Primary, Danger, Ghost }

    /// <summary>Включить общий стиль. Вызывать в начале каждого OnGUI.</summary>
    // ---------- Ввод текста ----------
    static int textWindowFrame = -100;

    /// <summary>Окно с полями ввода (чат, рынок, аккаунт) вызывает это в своём OnGUI каждый кадр.</summary>
    public static void MarkTextWindow() { textWindowFrame = Time.frameCount; }

    /// <summary>Игрок печатает в поле ввода - горячие клавиши игры (F, B, T, 1-9…) не срабатывают.</summary>
    public static bool Typing => GUIUtility.keyboardControl != 0 && Time.frameCount - textWindowFrame <= 2;

    /// <summary>
    /// Сейчас идёт OnGUI. Event.current бывает не пустым и вне OnGUI (остаётся с прошлого кадра) - тогда
    /// GUI.skin падал с «You can only call GUI functions from inside OnGUI». Глубина GUI вне OnGUI всегда 0.
    /// </summary>
    public static bool InGui
    {
        get
        {
            if (Event.current == null) return false;
            if (guiDepthProp == null && !guiDepthLooked)
            {
                guiDepthLooked = true;   // GUIUtility.guiDepth в Unity внутреннее - читаем его так
                guiDepthProp = typeof(GUIUtility).GetProperty("guiDepth", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            }
            if (guiDepthProp != null)
            {
                try { return (int)guiDepthProp.GetValue(null) > 0; }
                catch (System.Exception) { guiDepthProp = null; }
            }
            // Запасной путь: спросить GUI.skin (вне OnGUI он бросает исключение), ответ запоминаем на кадр
            if (probeFrame != Time.frameCount || probeType != Event.current.type)
            {
                probeFrame = Time.frameCount; probeType = Event.current.type;
                try { probeOk = GUI.skin != null; } catch (System.ArgumentException) { probeOk = false; }
            }
            return probeOk;
        }
    }
    static System.Reflection.PropertyInfo guiDepthProp;
    static bool guiDepthLooked, probeOk;
    static int probeFrame = -1;
    static EventType probeType;

    public static void Apply()
    {
        if (!InGui) return;   // вне OnGUI трогать GUI нельзя (ArgumentException)
        if (skin == null) BuildSkin();
        GUI.skin = skin;
        GUI.matrix = Matrix;
    }

    /// <summary>Только стиль, без масштаба (для свойств-стилей: их берут и под своей матрицей, например на заставке).</summary>
    static void EnsureSkin()
    {
        if (!InGui) return;   // вне OnGUI GUI.skin трогать нельзя (ArgumentException)
        if (skin == null) BuildSkin();
        GUI.skin = skin;
    }

    // ---------- Размер интерфейса ----------
    // Все окна рисуются в «точках окон». На компьютере 1 точка = 1 пиксель (или больше, если в настройках
    // увеличен «Размер интерфейса»). На телефоне точка - примерно как точка Android (dp): экран 2340×1080
    // становится ~900×430 точек, и кнопки получаются размером с палец. Apply() ставит GUI.matrix:
    // сдвиг на безопасную зону (без выреза камеры и закруглённых углов) и масштаб. Поэтому в коде окон
    // вместо Screen.width / Screen.height - UiKit.W / UiKit.H, а точка экрана → точка окна - UiKit.ToGui.

    static float scale = 1f;
    static Rect safePx;                 // безопасная зона в пикселях, y сверху
    static int calcFrame = -1, calcW, calcH;
    static Matrix4x4 matrix = Matrix4x4.identity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetScale() { calcFrame = -1; scale = 1f; winNow.Clear(); winLast.Clear(); winFrame = -1; }

    /// <summary>Телефон или планшет (или в редакторе включено сенсорное управление): крупные кнопки, жесты.</summary>
    public static bool Phone => Application.isMobilePlatform || TouchInput.Active;

    static void Calc()
    {
        int f = Time.frameCount;
        if (f == calcFrame && Screen.width == calcW && Screen.height == calcH) return;
        calcFrame = f; calcW = Screen.width; calcH = Screen.height;

        float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
        Rect sa = Screen.safeArea;
        if (sa.width < sw * 0.5f || sa.height < sh * 0.5f || sa.xMax > sw + 1f || sa.yMax > sh + 1f) sa = new Rect(0f, 0f, sw, sh);
        safePx = new Rect(sa.x, sh - sa.yMax, sa.width, sa.height);

        float s = 1f;
        if (Phone)
        {
            // Высота экрана в dp (по плотности экрана); окна рассчитаны минимум на ~430 точек высоты.
            // Маленький телефон - 430 (всё влезает), планшет - побольше, но не крупнее, чем «как на ноутбуке».
            float shortSide = Mathf.Min(sw, sh);
            float dpi = Screen.dpi;
            float dpH = dpi > 60f ? shortSide / (dpi / 160f) : 430f;
            float targetH = Mathf.Clamp(dpH, 430f, 720f);
            s = shortSide / targetH;
        }
        s *= GameSettings.UiSize;
        // Как бы ни выставили «Размер интерфейса», экран не становится меньше 640×360 точек:
        // все окна и кнопки (и ⚙, чтобы вернуть размер) остаются на экране
        s = Mathf.Clamp(s, 0.5f, Mathf.Max(0.5f, Mathf.Min(safePx.height / 360f, safePx.width / 640f)));
        scale = s;
        matrix = Matrix4x4.TRS(new Vector3(Mathf.Round(safePx.x), Mathf.Round(safePx.y), 0f), Quaternion.identity, new Vector3(s, s, 1f));
    }

    /// <summary>GUI.matrix для окон: сдвиг на безопасную зону и масштаб.</summary>
    public static Matrix4x4 Matrix { get { Calc(); return matrix; } }
    /// <summary>Сколько пикселей экрана в одной точке окон.</summary>
    public static float Scale { get { Calc(); return scale; } }
    /// <summary>Ширина экрана в точках окон (безопасная зона).</summary>
    public static float W { get { Calc(); return safePx.width / scale; } }
    /// <summary>Высота экрана в точках окон (безопасная зона).</summary>
    public static float H { get { Calc(); return safePx.height / scale; } }
    /// <summary>Весь экран вместе с вырезом камеры, в точках окон (для затемнения и фона).</summary>
    public static Rect FullRect { get { Calc(); return new Rect(-safePx.x / scale, -safePx.y / scale, Screen.width / scale, Screen.height / scale); } }
    /// <summary>Точка экрана (пиксели, y снизу: мышь, WorldToScreenPoint) → точка окна (y сверху).</summary>
    public static Vector2 ToGui(Vector2 screen) { Calc(); return new Vector2((screen.x - safePx.x) / scale, (Screen.height - screen.y - safePx.y) / scale); }
    public static Vector2 ToGui(Vector3 screen) { return ToGui((Vector2)screen); }
    /// <summary>Точка окна → точка экрана (пиксели, y снизу).</summary>
    public static Vector2 ToScreen(Vector2 gui) { Calc(); return new Vector2(gui.x * scale + safePx.x, Screen.height - (gui.y * scale + safePx.y)); }
    /// <summary>Мышь (или палец) в точках окон.</summary>
    public static Vector2 MouseGui
    {
        get
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null ? ToGui(m.position.ReadValue()) : new Vector2(-9999f, -9999f);
        }
    }
    /// <summary>Сколько точек окон в одной клетке карты (для надписей над зданиями).</summary>
    public static float PointsPerUnit(Camera cam) { return cam != null && cam.orthographicSize > 0.001f ? Screen.height / (2f * cam.orthographicSize) / Scale : 1f; }
    // Прежние названия из telefon-2
    public static Vector3 Px(Vector3 screen) { return screen; }
    public static Vector2 Px(Vector2 screen) { return screen; }

    /// <summary>
    /// Большое окно по центру экрана, не больше w×h. На телефоне - от полосы казны (там кнопки «Город», «Путь»,
    /// ⚙ остаются доступны) почти до низа экрана; на компьютере - с полями по краям.
    /// </summary>
    public static Rect CenterWindow(float w, float h)
    {
        bool phone = Phone;
        float top = phone ? 42f : 8f, bottom = phone ? 4f : 8f;
        w = Mathf.Min(w, W - (phone ? 12f : 40f));
        h = Mathf.Min(h, H - top - bottom);
        float y = Mathf.Clamp((H - h) / 2f, top, Mathf.Max(top, H - bottom - h));
        return new Rect(Mathf.Round((W - w) / 2f), Mathf.Round(y), w, h);
    }

    /// <summary>Сколько высоты можно отдать окну, что начинается с y (на телефоне - почти до низа экрана).</summary>
    public static float FreeH(float y, float desktopBottom) { return H - y - (Phone ? 4f : desktopBottom); }

    /// <summary>Кнопки в правом верхнем углу («Клан», «Путь», «Город», ⚙): на телефоне выше - под палец.</summary>
    public static float TopBtnY => Phone ? 4f : 10f;
    public static float TopBtnH => Phone ? 34f : 26f;

    /// <summary>Мелкая кнопка (крестик окна и т.п.) на телефоне делается крупнее, чтобы попасть пальцем.</summary>
    public static float Touch(float desktop, float phone) { return Phone ? phone : desktop; }

    public static GUISkin Skin { get { if (skin == null && InGui) BuildSkin(); return skin; } }
    public static GUIStyle Title { get { EnsureSkin(); return title; } }
    public static GUIStyle SectionStyle { get { EnsureSkin(); return section; } }
    public static GUIStyle Body { get { EnsureSkin(); return body; } }
    public static GUIStyle Small { get { EnsureSkin(); return small; } }
    public static GUIStyle MutedStyle { get { EnsureSkin(); return muted; } }
    public static GUIStyle Center { get { EnsureSkin(); return center; } }

    static Texture2D Tex(Color fill, Color edge, int radius = 3, int size = 16)
    {
        return Shaded(fill, fill, edge, radius, size, 0f);
    }

    /// <summary>
    /// Скруглённая плашка с гладким краем: заливка сверху вниз от top к bottom, рамка edge,
    /// тонкий блик под верхним краем (shine 0..1). Рисуется программно, 9-slice по радиусу.
    /// </summary>
    public static Texture2D Shaded(Color top, Color bottom, Color edge, int radius, int size, float shine)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color[size * size];
        float half = size * 0.5f, r = Mathf.Max(1f, radius);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Расстояние до края скруглённого квадрата (меньше 0 - внутри)
                float qx = Mathf.Abs(x + 0.5f - half) - (half - r), qy = Mathf.Abs(y + 0.5f - half) - (half - r);
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                float cover = Mathf.Clamp01(0.5f - d);
                if (cover <= 0f) { px[y * size + x] = new Color(0f, 0f, 0f, 0f); continue; }
                float k = y / (float)(size - 1);                   // 0 - низ, 1 - верх
                Color c = Color.Lerp(bottom, top, k);
                // Блик под верхним краем и чуть темнее у нижнего
                if (shine > 0f)
                {
                    if (d > -2.6f && d < -1.2f && y > size / 2) c = Color.Lerp(c, Color.white, 0.22f * shine);
                    if (d > -2.6f && d < -1.2f && y < size / 2) c = Color.Lerp(c, Color.black, 0.18f * shine);
                }
                float edgeK = Mathf.Clamp01(d + 1.6f);              // рамка ~1.3 точки
                c = Color.Lerp(c, edge, edgeK);
                c.a *= cover;
                px[y * size + x] = c;
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Color Mul(Color c, float k) { return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a); }

    static GUIStyle Btn(GUIStyle from, Color fill, Color edge, Color txt)
    {
        var s = new GUIStyle(from);
        // Объёмная кнопка: светлее сверху, темнее снизу; при наведении ярче, нажатая - «вдавлена»
        s.normal.background = Shaded(Mul(fill, 1.22f), Mul(fill, 0.82f), edge, 4, 20, 1f);
        s.hover.background = Shaded(Mul(fill, 1.45f), Mul(fill, 1.0f), Mul(edge, 1.25f), 4, 20, 1f);
        s.active.background = Shaded(Mul(fill, 0.70f), Mul(fill, 0.95f), edge, 4, 20, 0f);
        s.focused.background = s.normal.background;
        s.onNormal.background = Shaded(Mul(fill, 1.55f), Mul(fill, 1.1f), Accent, 4, 20, 1f);
        s.onHover.background = s.onNormal.background;
        s.onActive.background = s.active.background;
        s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = txt;
        s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = txt;
        s.border = new RectOffset(5, 5, 5, 5);
        s.padding = new RectOffset(8, 8, 4, 4);
        s.margin = new RectOffset(2, 2, 2, 2);
        s.fontSize = 13;
        s.richText = true;
        s.wordWrap = false;
        s.alignment = TextAnchor.MiddleCenter;
        return s;
    }

    static void BuildSkin()
    {
        skin = Object.Instantiate(GUI.skin);
        skin.hideFlags = HideFlags.HideAndDontSave;
        // На телефоне у шрифта Unity нет значков (✥ ✖ ◈ ⚔ …) - вместо них квадратики. Свой шрифт: буквы как Arial + значки
        if (Application.isMobilePlatform)
        {
            Font f = Resources.Load<Font>("Fonts/StrategycaSans-Regular");
            if (f != null) skin.font = f;
        }
        white = Texture2D.whiteTexture;

        // Окна
        skin.box = new GUIStyle(skin.box);
        // Окно: тёмное дерево-графит, сверху чуть светлее, по краю - бронзовая рамка
        // final-13: тёплое тёмное дерево вместо серого графита - окна как в игре, а не как в программе
        skin.box.normal.background = Shaded(new Color(0.165f, 0.140f, 0.115f, 0.97f), new Color(0.085f, 0.072f, 0.062f, 0.97f), PanelEdge, 7, 32, 0.6f);
        skin.box.normal.textColor = Accent;
        skin.box.border = new RectOffset(8, 8, 8, 8);
        skin.box.padding = new RectOffset(8, 8, 7, 6);
        skin.box.fontSize = 14;
        skin.box.fontStyle = FontStyle.Bold;
        skin.box.alignment = TextAnchor.UpperCenter;
        skin.box.richText = true;
        skin.window = new GUIStyle(skin.box);

        // Кнопки
        skin.button = Btn(skin.button, new Color(0.33f, 0.295f, 0.255f), new Color(0.56f, 0.48f, 0.36f), Text);   // final-13: тёплый камень
        btnPrimary = Btn(skin.button, new Color(0.62f, 0.48f, 0.18f), new Color(0.90f, 0.74f, 0.38f), Color.white);
        btnDanger = Btn(skin.button, new Color(0.55f, 0.20f, 0.18f), new Color(0.85f, 0.38f, 0.33f), Color.white);
        // «Отмена», «Назад», «Закрыть»: тёмная, но надпись светлая - чтобы не казалась выключенной
        btnGhost = Btn(skin.button, new Color(0.15f, 0.13f, 0.115f, 0.9f), new Color(0.36f, 0.31f, 0.25f), new Color(0.84f, 0.81f, 0.76f));
        tab = Btn(skin.button, new Color(0.19f, 0.165f, 0.14f), new Color(0.34f, 0.29f, 0.23f), new Color(0.74f, 0.70f, 0.63f));
        tabOn = Btn(skin.button, new Color(0.34f, 0.29f, 0.18f), Accent, Accent);
        tabOn.fontStyle = FontStyle.Bold;
        close = Btn(skin.button, new Color(0.30f, 0.16f, 0.13f), new Color(0.55f, 0.36f, 0.28f), new Color(0.95f, 0.85f, 0.78f));   // final-13: красноватый крестик
        close.padding = new RectOffset(0, 0, 0, 2);
        close.fontSize = 15;

        // Текст
        skin.label = new GUIStyle(skin.label) { fontSize = 13, richText = true, wordWrap = true };
        skin.label.normal.textColor = Text;
        skin.label.padding = new RectOffset(0, 0, 1, 1);
        body = skin.label;
        small = new GUIStyle(skin.label) { fontSize = 11 };
        small.normal.textColor = new Color(0.80f, 0.80f, 0.80f);
        muted = new GUIStyle(skin.label) { fontSize = 12 };
        muted.normal.textColor = Muted;
        center = new GUIStyle(skin.label) { alignment = TextAnchor.MiddleCenter };
        title = new GUIStyle(skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        title.normal.textColor = Accent;
        section = new GUIStyle(skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.LowerLeft };
        section.normal.textColor = new Color(0.85f, 0.75f, 0.52f);
        fold = new GUIStyle(skin.label) { fontSize = 12, wordWrap = false, alignment = TextAnchor.MiddleLeft };
        fold.normal.textColor = Info;
        fold.hover.textColor = Color.white;

        // Переключатели и поля
        skin.toggle = new GUIStyle(skin.toggle) { fontSize = 13, richText = true };
        skin.toggle.normal.textColor = skin.toggle.hover.textColor = skin.toggle.onNormal.textColor = skin.toggle.onHover.textColor = Text;
        skin.textField = new GUIStyle(skin.textField) { fontSize = 13 };
        skin.textField.normal.background = Tex(new Color(0.06f, 0.06f, 0.07f), new Color(0.35f, 0.35f, 0.38f), 2);
        skin.textField.focused.background = Tex(new Color(0.06f, 0.06f, 0.07f), Accent, 2);
        skin.textField.normal.textColor = skin.textField.focused.textColor = Text;
        skin.textField.border = new RectOffset(3, 3, 3, 3);

        // Полосы прокрутки - тонкие
        float barW = Application.isMobilePlatform ? 20f : 10f;   // ползунок на телефоне - под палец
        skin.verticalScrollbar = new GUIStyle(skin.verticalScrollbar) { fixedWidth = barW };
        skin.verticalScrollbar.normal.background = Tex(new Color(0.08f, 0.08f, 0.09f, 0.8f), new Color(0.08f, 0.08f, 0.09f, 0.8f), 3);
        skin.verticalScrollbar.border = new RectOffset(3, 3, 3, 3);
        skin.verticalScrollbarThumb = new GUIStyle(skin.verticalScrollbarThumb) { fixedWidth = barW };
        if (Application.isMobilePlatform) skin.verticalScrollbarThumb.fixedHeight = 0f;
        skin.verticalScrollbarThumb.normal.background = Tex(new Color(0.55f, 0.46f, 0.32f), new Color(0.55f, 0.46f, 0.32f), 3);
        skin.verticalScrollbarThumb.border = new RectOffset(3, 3, 3, 3);

        tipStyle = new GUIStyle(skin.label) { fontSize = 12, wordWrap = true };
        tipStyle.normal.background = Tex(new Color(0.09f, 0.075f, 0.06f, 0.97f), Accent * 0.8f, 4);
        tipStyle.border = new RectOffset(4, 4, 4, 4);
        tipStyle.padding = new RectOffset(8, 8, 6, 6);
        tipStyle.normal.textColor = Text;
    }

    // ---------- Окно ----------

    /// <summary>
    /// Окно с шапкой. Возвращает прямоугольник содержимого (с отступами). close - нажали «×».
    /// showClose=false - без крестика.
    /// </summary>
    // ---------- Где открыты окна ----------
    // Окна (UiKit.Window) отмечают свои прямоугольники каждый кадр. По ним плашка часов наверху
    // понимает, что её закрыло окно, и не просвечивает сквозь него и не ловит нажатия.

    static readonly List<Rect> winNow = new List<Rect>(), winLast = new List<Rect>();
    static int winFrame = -1;

    static void NoteWindow(Rect r)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        if (winFrame != Time.frameCount) { winLast.Clear(); winLast.AddRange(winNow); winNow.Clear(); winFrame = Time.frameCount; }
        winNow.Add(LocalToTop(r));   // окно может быть внутри группы или прокрутки
    }

    // ---------- Свои координаты группы / прокрутки ↔ точки окон ----------
    // GUIUtility.GUIToScreenPoint учитывает сдвиг групп и прокруток, а вот масштаб GUI.matrix - смотря по версии Unity.
    // Поэтому проверяем на ходу: если отрезок в 100 точек вышел длиной ~100, масштаб не учтён - домножаем сами.

    static bool ClipHasMatrix()
    {
        float k = GUIUtility.GUIToScreenPoint(new Vector2(100f, 0f)).x - GUIUtility.GUIToScreenPoint(Vector2.zero).x;
        float m = GUI.matrix.m00;
        return Mathf.Abs(k - 100f * m) <= Mathf.Abs(k - 100f);
    }

    /// <summary>Точка в координатах текущей группы / прокрутки → точки окон верхнего уровня.</summary>
    public static Vector2 LocalToTop(Vector2 local)
    {
        Vector2 s = GUIUtility.GUIToScreenPoint(local);
        if (!ClipHasMatrix()) return s;   // уже в точках окон
        return ToGui(new Vector2(s.x, Screen.height - s.y));
    }

    public static Rect LocalToTop(Rect r)
    {
        Vector2 a = LocalToTop(new Vector2(r.xMin, r.yMin)), b = LocalToTop(new Vector2(r.xMax, r.yMax));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>Точки окон верхнего уровня → координаты текущей группы / прокрутки.</summary>
    public static Vector2 TopToLocal(Vector2 top)
    {
        if (!ClipHasMatrix()) return GUIUtility.ScreenToGUIPoint(top);
        Vector2 px = ToScreen(top);   // y снизу
        return GUIUtility.ScreenToGUIPoint(new Vector2(px.x, Screen.height - px.y));
    }

    /// <summary>Прямоугольник (точки окон) закрыт каким-нибудь окном - в этом или прошлом кадре.</summary>
    public static bool UnderWindow(Rect r)
    {
        if (winFrame < Time.frameCount - 2) return false;
        foreach (var w in winLast) if (w.Overlaps(r)) return true;
        if (winFrame == Time.frameCount) foreach (var w in winNow) if (w.Overlaps(r)) return true;
        return false;
    }

    public static Rect Window(Rect r, string caption, out bool close, bool showClose = true)
    {
        EnsureSkin();
        NoteWindow(r);
        GameAudio.NoteWindow();   // звук открытия / закрытия окна
        // Мягкая тень под окном: окно «лежит» над картой, а не сливается с ней
        if (Event.current.type == EventType.Repaint)
        {
            Color sc = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.30f * sc.a);
            skin.box.Draw(new Rect(r.x + 2f, r.y + 4f, r.width + 2f, r.height + 2f), GUIContent.none, false, false, false, false);
            GUI.color = sc;
        }
        LocGUI.Box(r, GUIContent.none);
        // Шапка: чуть светлее, золотая линия снизу
        Color old = GUI.color;
        // Шапка: мягкий отсвет сверху вниз, под ней золотая линия, которая тает к краям, и ромбик посередине
        GUI.color = new Color(1f, 0.93f, 0.78f, 0.09f);
        GUI.DrawTexture(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, TitleH - 3f), Fade(false));
        GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
        GUI.DrawTexture(new Rect(r.x + 6f, r.y + TitleH, r.width - 12f, 1f), Fade(true));
        // final-13: ромбик посередине золотой линии и золотые заклёпки по углам - окно как рамка из игры
        if (Event.current.type == EventType.Repaint)
        {
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
            Diamond(new Vector2(r.center.x, r.y + TitleH + 0.5f), 4f);
            GUI.color = new Color(0.80f, 0.64f, 0.36f, 0.9f);
            float k = 5.5f;
            Diamond(new Vector2(r.x + k, r.y + k), 2.4f);
            Diamond(new Vector2(r.xMax - k, r.y + k), 2.4f);
            Diamond(new Vector2(r.x + k, r.yMax - k), 2.4f);
            Diamond(new Vector2(r.xMax - k, r.yMax - k), 2.4f);
        }
        GUI.color = old;
        bool big = Phone;   // на телефоне крестик крупнее - под палец
        LocGUI.Label(new Rect(r.x + Pad, r.y + 2f, r.width - Pad * 2f - (showClose ? (big ? 38f : 26f) : 0f), TitleH - 2f), caption, title);
        close = showClose && LocGUI.Button(big ? new Rect(r.xMax - 40f, r.y + 2f, 36f, TitleH - 3f) : new Rect(r.xMax - 28f, r.y + 5f, 22f, 20f), "×", UiKit.close);
        return new Rect(r.x + Pad, r.y + TitleH + 6f, r.width - Pad * 2f, r.height - TitleH - 6f - Pad * 0.6f);
    }

    public static Rect Window(Rect r, string caption) { return Window(r, caption, out _, false); }

    /// <summary>Крестик «×» в шапке окна, нарисованного без своего крестика (окна построек, жителей, лагерей). true - нажали.</summary>
    public static bool CloseButton(Rect window)
    {
        EnsureSkin();
        bool big = Phone;
        return LocGUI.Button(big ? new Rect(window.xMax - 40f, window.y + 2f, 36f, TitleH - 3f) : new Rect(window.xMax - 28f, window.y + 5f, 22f, 20f), "×", close);
    }

    static Texture2D fadeV, fadeH, diamond;

    /// <summary>Маленький ромб (украшение рамок), цвет - GUI.color.</summary>
    public static void Diamond(Vector2 c, float r)
    {
        if (diamond == null)
        {
            const int N = 16;
            diamond = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Abs(x + 0.5f - N / 2f) + Mathf.Abs(y + 0.5f - N / 2f);
                    float a = Mathf.Clamp01(N / 2f - d);
                    float shade = y > N / 2 ? 1f : 0.78f;
                    diamond.SetPixel(x, y, new Color(shade, shade, shade, a));
                }
            diamond.Apply();
        }
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), diamond);
    }

    /// <summary>Белая полоска с прозрачными концами: vertical=false - сверху вниз, true - к левому и правому краю.</summary>
    static Texture2D Fade(bool toSides)
    {
        Texture2D t = toSides ? fadeH : fadeV;
        if (t != null) return t;
        const int N = 64;
        t = toSides ? new Texture2D(N, 1, TextureFormat.RGBA32, false) : new Texture2D(1, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.hideFlags = HideFlags.HideAndDontSave;
        for (int i = 0; i < N; i++)
        {
            float k = i / (float)(N - 1);
            float a = toSides ? Mathf.Clamp01(Mathf.Min(k, 1f - k) * 5f) : k * k;   // у шапки: ярче к верху
            if (toSides) t.SetPixel(i, 0, new Color(1f, 1f, 1f, a)); else t.SetPixel(0, i, new Color(1f, 1f, 1f, a));
        }
        t.Apply();
        if (toSides) fadeH = t; else fadeV = t;
        return t;
    }

    /// <summary>Высота окна по высоте содержимого.</summary>
    public static float WindowH(float contentH) { return TitleH + 6f + contentH + Pad * 0.6f; }

    // ---------- Разделы ----------

    /// <summary>Подзаголовок раздела с тонкой линией. Возвращает y под ним.</summary>
    public static float Section(float x, float y, float w, string caption)
    {
        EnsureSkin();
        // final-13: золотой ромбик перед названием раздела и линия, тающая к краю
        Color old = GUI.color;
        if (Event.current.type == EventType.Repaint)
        {
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.9f);
            Diamond(new Vector2(x + 4f, y + (SectionH - 4f) * 0.62f), 3.2f);
        }
        GUI.color = old;
        LocGUI.Label(new Rect(x + 12f, y, w - 12f, SectionH - 4f), Loc.T(caption).ToUpper(), section);
        GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.28f);
        GUI.DrawTexture(new Rect(x, y + SectionH - 3f, w, 1f), Fade(true));
        GUI.color = old;
        return y + SectionH;
    }

    public static void Line(float x, float y, float w)
    {
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.10f);
        GUI.DrawTexture(new Rect(x, y, w, 1f), white);
        GUI.color = old;
    }

    /// <summary>Строка «название ........ значение».</summary>
    public static void Row(Rect r, string name, string value)
    {
        EnsureSkin();
        LocGUI.Label(r, name, muted);
        var right = new GUIStyle(body) { alignment = TextAnchor.UpperRight, wordWrap = false };
        LocGUI.Label(r, value, right);
    }

    // ---------- Кнопки ----------

    public static GUIStyle ButtonStyle(Kind k)
    {
        EnsureSkin();
        switch (k)
        {
            case Kind.Primary: return btnPrimary;
            case Kind.Danger: return btnDanger;
            case Kind.Ghost: return btnGhost;
            default: return skin.button;
        }
    }

    public static bool Button(Rect r, string text, Kind k = Kind.Normal, string tip = null)
    {
        bool b = LocGUI.Button(r, text, ButtonStyle(k));
        if (!string.IsNullOrEmpty(tip)) Tip(r, tip);
        return b;
    }

    /// <summary>Кнопка, которая может быть недоступна (серая) с причиной в подсказке.</summary>
    public static bool Button(Rect r, string text, bool enabled, Kind k, string tip)
    {
        bool en = GUI.enabled;
        GUI.enabled = en && enabled;
        bool b = LocGUI.Button(r, text, ButtonStyle(k));
        GUI.enabled = en;
        if (!string.IsNullOrEmpty(tip)) Tip(r, tip);
        return b && enabled;
    }

    /// <summary>
    /// Кнопка с картинкой вещи слева (ItemIcons) и подписью. Недоступная - серая и бледная, причина - в подсказке.
    /// Так выглядят все «выбери вид»: ядра пушки, снаряды полевой пушки (final-4).
    /// </summary>
    public static bool IconButton(Rect r, string icon, string text, bool enabled, Kind k, string tip)
    {
        bool b = Button(r, "", enabled, k, tip);
        float s = Mathf.Min(r.height - 6f, 26f);
        Color old = GUI.color;
        if (!enabled) GUI.color = new Color(old.r, old.g, old.b, old.a * 0.45f);
        bool hasIcon = !string.IsNullOrEmpty(icon) && ItemIcons.Has(icon);
        if (hasIcon) ItemIcons.Draw(new Rect(r.x + 4f, r.y + (r.height - s) * 0.5f, s, s), icon);
        GUI.color = old;
        float lx = hasIcon ? s + 8f : 6f;
        GUIStyle st = new GUIStyle(Small) { alignment = hasIcon ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(r.x + lx, r.y, r.width - lx - 4f, r.height), enabled ? text : Col(MutedHex, text), st);
        return b;
    }

    // ---------- Вкладки ----------

    public static int Tabs(Rect r, string[] labels, int current)
    {
        EnsureSkin();
        if (labels == null || labels.Length == 0) return current;
        float w = r.width / labels.Length;
        for (int i = 0; i < labels.Length; i++)
        {
            Rect b = new Rect(r.x + i * w, r.y, w - 2f, r.height);
            if (LocGUI.Button(b, labels[i], i == current ? tabOn : tab)) current = i;
        }
        return current;
    }

    // ---------- Полоска ----------

    public static void Bar(Rect r, float frac, Color fill, string text = null)
    {
        EnsureSkin();
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(r, white);
        GUI.color = fill;
        Rect f = new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(frac), r.height - 2f);
        GUI.DrawTexture(f, white);
        // Блик сверху - полоска выглядит объёмной
        GUI.color = new Color(1f, 1f, 1f, 0.22f);
        GUI.DrawTexture(new Rect(f.x, f.y, f.width, f.height * 0.5f), Fade(false));
        GUI.color = old;
        if (!string.IsNullOrEmpty(text))
        {
            var st = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            st.normal.textColor = Color.white;
            LocGUI.Label(r, text, st);
        }
    }

    // ---------- Сворачиваемый блок ----------

    static readonly HashSet<string> openFolds = new HashSet<string>();

    public static bool IsOpen(string id) { return openFolds.Contains(id); }
    public static void SetOpen(string id, bool on) { if (on) openFolds.Add(id); else openFolds.Remove(id); }

    /// <summary>Строка «▸ Подробнее» / «▾ Подробнее». Возвращает, раскрыт ли блок. Состояние помнится по id.</summary>
    public static bool Fold(Rect r, string id, string caption = "Подробнее")
    {
        EnsureSkin();
        bool on = openFolds.Contains(id);
        if (LocGUI.Button(r, (on ? "▾ " : "▸ ") + caption, fold)) { on = !on; SetOpen(id, on); }
        return on;
    }

    // ---------- Прокрутка ----------

    static readonly Dictionary<string, Vector2> scrolls = new Dictionary<string, Vector2>();

    /// <summary>
    /// Содержимое высотой contentH в области view. Влезает - рисуется как есть; не влезает - с тонкой полосой
    /// прокрутки справа (колёсико тоже крутит). draw получает прямоугольник содержимого: внутри прокрутки
    /// он начинается с (0, 0), поэтому считайте координаты от его x и y. Положение прокрутки помнится по id.
    /// </summary>
    public static void Scroll(string id, Rect view, float contentH, System.Action<Rect> draw)
    {
        EnsureSkin();
        if (contentH <= view.height + 0.5f)
        {
            scrolls.Remove(id);
            draw(new Rect(view.x, view.y, view.width, contentH));
            return;
        }
        // Колёсико над прокруткой крутит её, а не приближает карту
        if (Event.current != null && view.Contains(Event.current.mousePosition)) CameraController.BlockZoom();
        scrolls.TryGetValue(id, out Vector2 s);
        Rect content = new Rect(0f, 0f, view.width - ScrollBarW, contentH);
        s = BeginScrollView(view, s, content, false, false, GUIStyle.none, skin.verticalScrollbar);
        draw(content);
        GUI.EndScrollView();
        scrolls[id] = s;
    }

    // ---------- Прокрутка пальцем ----------
    // Все списки зовут UiKit.BeginScrollView вместо GUI.BeginScrollView. На телефоне список, на котором начали
    // вести пальцем, каждый кадр сдвигается ровно на путь пальца (раньше - через колесо мыши, рывками и не всегда).

    static readonly int TouchScrollHash = "UiKitTouchScroll".GetHashCode();
    static int scrollOwner = -1, pickGesture = -1, pickFrame = -1;

    public static Vector2 BeginScrollView(Rect view, Vector2 pos, Rect content)
    { content = FitContent(view, content, null); return GUI.BeginScrollView(view, TouchScroll(view, pos, content), content); }

    public static Vector2 BeginScrollView(Rect view, Vector2 pos, Rect content, GUIStyle hBar, GUIStyle vBar)
    { content = FitContent(view, content, vBar); return GUI.BeginScrollView(view, TouchScroll(view, pos, content), content, hBar, vBar); }

    public static Vector2 BeginScrollView(Rect view, Vector2 pos, Rect content, bool alwaysH, bool alwaysV)
    { content = FitContent(view, content, null); return GUI.BeginScrollView(view, TouchScroll(view, pos, content), content, alwaysH, alwaysV); }

    public static Vector2 BeginScrollView(Rect view, Vector2 pos, Rect content, bool alwaysH, bool alwaysV, GUIStyle hBar, GUIStyle vBar)
    { content = FitContent(view, content, vBar); return GUI.BeginScrollView(view, TouchScroll(view, pos, content), content, alwaysH, alwaysV, hBar, vBar); }

    /// <summary>
    /// Почему на телефоне не была видна последняя строка списков и чата клана: на телефоне полоса прокрутки
    /// толще (20 точек), а списки оставляли под неё 14-16 точек. Содержимое оказывалось шире видимой части,
    /// Unity добавлял снизу ещё и горизонтальную полосу, и она закрывала последнюю строку (а дальше её
    /// было не докрутить). Теперь содержимое списка сужается под настоящую ширину полосы, а снизу
    /// добавляется небольшой запас, чтобы последняя строка вставала целиком над краем окна.
    /// Списки, которые нарочно шире окна (листаются вбок), не трогаем.
    /// </summary>
    static Rect FitContent(Rect view, Rect content, GUIStyle vBar)
    {
        if (content.height <= view.height + 0.5f) return content;          // прокрутки нет
        if (content.width > view.width + 0.5f) return content;             // нарочно листается вбок
        GUIStyle vb = vBar ?? (skin != null ? skin.verticalScrollbar : GUI.skin.verticalScrollbar);
        float bw = vb != null && vb != GUIStyle.none ? (vb.fixedWidth > 0f ? vb.fixedWidth : 12f) + vb.margin.horizontal : 0f;
        float maxW = Mathf.Max(10f, view.width - bw - 1f);
        if (content.width > maxW) content.width = maxW;
        content.height += Phone ? 14f : 4f;
        return content;
    }

    static readonly List<Rect> barsNow = new List<Rect>(), barsLast = new List<Rect>();
    static int barsFrame = -1;

    /// <summary>Точка (точки окон) на полосе прокрутки какого-нибудь списка - в этом или прошлом кадре.</summary>
    public static bool OnScrollbar(Vector2 top)
    {
        if (barsFrame < Time.frameCount - 2) return false;
        foreach (var r in barsLast) if (r.Contains(top)) return true;
        foreach (var r in barsNow) if (r.Contains(top)) return true;
        return false;
    }

    static Vector2 TouchScroll(Rect view, Vector2 pos, Rect content)
    {
        if (content.height > view.height + 0.5f && Event.current != null && Event.current.type == EventType.Repaint && Phone)
        {
            if (barsFrame != Time.frameCount) { barsLast.Clear(); barsLast.AddRange(barsNow); barsNow.Clear(); barsFrame = Time.frameCount; }
            float bw = Skin.verticalScrollbar.fixedWidth > 0f ? Skin.verticalScrollbar.fixedWidth : 12f;
            barsNow.Add(LocalToTop(new Rect(view.xMax - bw - 6f, view.y, bw + 6f, view.height)));
        }
        // Номер списка: порядок в OnGUI + место на экране (у разных окон номера порядка могут совпасть)
        int id = GUIUtility.GetControlID(TouchScrollHash, FocusType.Passive) * 31
                 + Mathf.RoundToInt(view.x) * 7919 + Mathf.RoundToInt(view.y) * 104729 + Mathf.RoundToInt(view.width);
        if (!TouchInput.GuiScrolling || TouchInput.ScoutMapOpen || Event.current == null || Event.current.type != EventType.Repaint) return pos;

        // Первый кадр жеста: какой список под точкой, где коснулись (самый вложенный - он рисуется последним)
        int g = TouchInput.GestureId;
        if (g != pickGesture) { pickGesture = g; pickFrame = Time.frameCount; scrollOwner = -1; }
        if (Time.frameCount == pickFrame)
        {
            Vector2 local = TopToLocal(ToGui(TouchInput.StartPx));
            if (view.Contains(local) || view.Contains(Event.current.mousePosition)) scrollOwner = id;   // мышь Unity стоит под пальцем
        }
        if (id != scrollOwner) return pos;

        float d = TouchInput.ScrollDragGui;   // точки окон, палец вниз - плюс
        if (d == 0f) return pos;
        float max = Mathf.Max(0f, content.height - view.height);
        pos.y = Mathf.Clamp(pos.y - d, 0f, max);   // содержимое едет вместе с пальцем
        return pos;
    }

    /// <summary>Сколько ширины забирает полоса прокрутки (вместе с зазором до текста).</summary>
    public static float ScrollBarW => Phone ? 24f : 14f;   // на телефоне ползунок толще - чтобы взять пальцем

    // ---------- Одно окно за раз ----------
    // Меню «Город», настройки, клан, путь развития, постройки, ловушки и т.п. открываются на одном и том же месте
    // (справа сверху или по центру) и раньше ложились друг на друга. Теперь окно при открытии зовёт Front("имя"),
    // а остальные в своём OnGUI видят Behind("своё имя") и закрываются сами.

    static string front;

    /// <summary>Окно открылось: остальные большие окна закроются сами.</summary>
    public static void Front(string id) { front = id; }

    /// <summary>Поверх открыто другое большое окно - этому пора закрыться.</summary>
    public static bool Behind(string id) { return front != null && front != id; }

    /// <summary>
    /// Для окна с флагом «открыто», которое открывают из разных мест (кнопка, клавиша): зовите в начале OnGUI.
    /// Только что открылось - встаёт вперёд; открыто, но впереди другое окно - возвращает false (закрыть).
    /// </summary>
    public static bool KeepFront(string id, bool open, ref bool wasOpen)
    {
        if (open && !wasOpen) Front(id);
        else if (open && Behind(id)) open = false;
        wasOpen = open;
        return open;
    }

    // ---------- Подсказки ----------

    static string tipText;
    static Vector2 tipScreen;
    static int tipFrame = -1;

    /// <summary>Показать подсказку, если мышь над r (r - в текущих координатах GUI, можно внутри групп и прокрутки).</summary>
    public static void Tip(Rect r, string text)
    {
        if (string.IsNullOrEmpty(text) || Event.current == null) return;
        if (Event.current.type != EventType.Repaint) return;
        if (!GameSettings.Tips) return;
        if (TouchInput.Active && !TouchInput.HoldingOnUi) return;   // телефон: подсказка - только если подержать палец
        if (!r.Contains(Event.current.mousePosition)) return;
        tipText = text;
        tipScreen = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
        tipFrame = Time.frameCount;
        EnsureDrawer();
    }

    static void EnsureDrawer()
    {
        if (UiTooltipDrawer.Instance == null)
        {
            var go = new GameObject("UiTooltips");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<UiTooltipDrawer>();
        }
    }

    /// <summary>Подсказка видна (в этом или прошлом кадре).</summary>
    public static bool TipShownRecently => Time.frameCount - tipFrame <= 1;

    internal static void DrawTip()
    {
        if (tipFrame != Time.frameCount || string.IsNullOrEmpty(tipText)) return;
        EnsureSkin();
        Vector2 m = GUIUtility.ScreenToGUIPoint(tipScreen);
        GUIStyle st = tipStyle;
        float w = 280f;
        if (Phone)
        {
            // На телефоне подсказку читают с вытянутой руки - крупнее и шире
            if (tipPhone == null) tipPhone = new GUIStyle(tipStyle) { fontSize = 16, padding = new RectOffset(12, 12, 9, 9) };
            st = tipPhone;
            float maxW = Mathf.Min(460f, W - 8f);
            w = Mathf.Clamp(st.CalcSize(Loc.Content(tipText)).x + 2f, 160f, maxW);
        }
        float h = st.CalcHeight(Loc.Content(tipText), w);
        float x = Mathf.Min(m.x + 16f, W - w - 4f);
        float y = m.y + 20f;
        if (Phone) { x = Mathf.Clamp(m.x - w * 0.5f, 4f, W - w - 4f); y = m.y - h - 34f; }   // над пальцем, а не под ним
        if (y + h > H - 4f) y = m.y - h - 8f;
        if (y < 4f) y = Mathf.Min(m.y + 34f, H - h - 4f);
        LocGUI.Label(new Rect(x, y, w, h), tipText, st);
    }

    /// <summary>Высота текста в стиле body при ширине w (для расчёта высоты окон).</summary>
    public static float TextH(string text, float w, GUIStyle st = null)
    {
        EnsureSkin();
        return (st ?? body).CalcHeight(Loc.Content(text), w);
    }
}

/// <summary>Рисует подсказки UiKit поверх всех окон.</summary>
public class UiTooltipDrawer : MonoBehaviour
{
    public static UiTooltipDrawer Instance { get; private set; }

    void Awake() { if (Instance == null) Instance = this; else Destroy(gameObject); }

    void OnGUI()
    {
        GUI.depth = -1000;
        if (Event.current.type != EventType.Repaint) return;
        UiKit.DrawTip();
    }
}
