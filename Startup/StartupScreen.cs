using UnityEngine;

/// <summary>
/// Запуск игры: заставка разработчика Cinetica Games, потом экран загрузки STRATEGYCA.
///
///  1. Логотип Cinetica Games (около 3,5 с, пропустить нельзя). Звук игры в это время молчит, звучит только заставка.
///  2. Экран загрузки: деревня, название игры, полоска и советы. Пока он открыт, игра в фоне подключается к серверу,
///     входит по сохранённой сессии и строит город. Даже если всё готово сразу, полоска идёт хотя бы MinLoading секунд.
///  3. Экран плавно гаснет: открывается вход в аккаунт или сразу город.
///
/// Рисует и решает, когда закончить, AccountManager: он знает, что сейчас с сервером, входом и городом
/// (StartupScreen.Draw). Отсюда же берутся фон и название для экрана входа и экрана «Связь потеряна».
/// Картинки: Resources/Startup (настройки импорта - Startup/Editor/StartupImport.cs).
/// </summary>
public static class StartupScreen
{
    enum Stage { Logo, Loading, Reveal, Done }

    // Заставка Cinetica Games
    const float LogoBlack = 0.35f;     // сначала чёрный экран
    const float LogoIn = 0.9f;         // знак проявляется
    const float WordDelay = 0.45f;     // надпись - чуть позже знака
    const float LogoHold = 1.6f;
    const float LogoOut = 0.6f;
    const float LogoTotal = LogoBlack + LogoIn + LogoHold + LogoOut;

    // Экран загрузки
    const float MinLoading = 2.8f;         // при запуске загрузка идёт хотя бы столько
    const float MinLoadingQuick = 1.2f;    // переход в военный город и обратно
    const float FadeFromBlack = 0.6f;
    const float FullHold = 0.3f;           // полоска заполнилась - чуть постоять
    const float RevealSeconds = 0.7f;
    const float TipSeconds = 5f;
    const float CityTimeout = 8f;          // город так и не построился - всё равно открываем

    static Stage stage = Stage.Done;
    static float stageAt, loadingAt, fullAt = -1f, soundFrom;
    static float minLoading = MinLoading;
    static float shown;                    // полоска на экране, 0..1
    static bool toLogin, stingPlayed, withLogo;
    static int tip;

    static Texture2D bg, title, emblem, wordmark, shade, vignette, barFill, band;
    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { stage = Stage.Done; shown = 0f; fullAt = -1f; stingPlayed = false; }

    /// <summary>Экран запуска ещё виден (полностью или гаснет).</summary>
    public static bool Active => stage != Stage.Done;
    /// <summary>Экран закрывает игру целиком: клики и клавиши до игры не доходят.</summary>
    public static bool Covering => stage == Stage.Logo || stage == Stage.Loading;
    /// <summary>Громкость музыки и фона: на заставке тишина, потом звук плавно появляется.</summary>
    public static float SoundGain
    {
        get
        {
            if (stage == Stage.Logo) return 0f;
            if (!withLogo) return 1f;
            return Mathf.Clamp01((Time.unscaledTime - soundFrom) / 2.5f);
        }
    }

    /// <summary>Запуск игры. logo - показать Cinetica Games (при переходе в военный город и обратно не показываем).</summary>
    public static void Begin(bool logo)
    {
        withLogo = logo;
        stage = logo ? Stage.Logo : Stage.Loading;
        stageAt = loadingAt = Time.unscaledTime;
        soundFrom = Time.unscaledTime;
        minLoading = logo ? MinLoading : MinLoadingQuick;
        shown = 0f;
        fullAt = -1f;
        toLogin = false;
        stingPlayed = false;
        tip = Random.Range(0, Tips.Length);
    }

    /// <summary>Убрать экран сразу (например, пропала связь посреди загрузки).</summary>
    public static void End() { stage = Stage.Done; UnloadLogo(); }

    // ---------- Кадр ----------

    /// <summary>
    /// Рисует экран запуска. status - что сейчас делаем, target - докуда может дойти полоска (0..1),
    /// ready - всё готово (login: дальше экран входа, иначе - город).
    /// Возвращает, где под названием есть место для подробностей (ошибка связи, адрес сервера).
    /// </summary>
    public static Rect Draw(string status, float target, bool ready, bool login)
    {
        Load();
        float now = Time.unscaledTime;
        bool repaint = Event.current.type == EventType.Repaint;
        if (repaint) Advance(now, target, ready, login);
        if (stage == Stage.Done) return new Rect();

        Color old = GUI.color;
        Rect free = new Rect();
        if (stage == Stage.Logo) DrawLogo(now - stageAt);
        else
        {
            // Гаснем: на вход - только полоска и надписи (фон тот же), в город - весь экран
            float a = stage == Stage.Reveal ? 1f - Smooth(Mathf.Clamp01((now - stageAt) / RevealSeconds)) : 1f;
            float sceneA = stage == Stage.Reveal && !toLogin ? a : 1f;
            if (stage == Stage.Reveal && toLogin) sceneA = 0f;   // фон уже рисует экран входа
            if (sceneA > 0f) DrawBackdrop(sceneA, 0f);
            float titleBottom = DrawTitle(stage == Stage.Reveal && toLogin ? 1f : sceneA);
            free = new Rect(0f, titleBottom + 10f, UiKit.W, Mathf.Max(0f, BarTop - titleBottom - 30f));
            DrawProgress(status, a);

            // Только что погас логотип - выходим из черноты
            if (withLogo)
            {
                float fb = 1f - Mathf.Clamp01((now - loadingAt) / FadeFromBlack);
                if (fb > 0f)
                {
                    GUI.color = new Color(0f, 0f, 0f, fb);
                    GUI.DrawTexture(UiKit.FullRect, Texture2D.whiteTexture);
                }
            }
        }
        GUI.color = old;

        // Клики не проходят в игру, пока экран закрывает её
        if (Covering)
        {
            EventType et = Event.current.type;
            if (et == EventType.MouseDown || et == EventType.MouseUp || et == EventType.ScrollWheel) Event.current.Use();
        }
        return free;
    }

    static void Advance(float now, float target, bool ready, bool login)
    {
        if (stage == Stage.Logo)
        {
            if (!stingPlayed && now - stageAt >= LogoBlack)
            {
                stingPlayed = true;
                Sfx.Ui("logo_cinetica");
            }
            if (now - stageAt >= LogoTotal)
            {
                stage = Stage.Loading;
                stageAt = loadingAt = soundFrom = now;
                UnloadLogo();
            }
            return;
        }
        if (stage == Stage.Loading)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float loadT = now - loadingAt;
            // Полоска не обгоняет время (хотя бы minLoading) и то, что уже сделано
            float goal = Mathf.Min(ready ? 1f : Mathf.Clamp(target, 0f, 0.97f), Mathf.Lerp(0.08f, 1f, Mathf.Clamp01(loadT / minLoading)));
            if (goal > shown) shown = Mathf.MoveTowards(shown, goal, dt * Mathf.Max(0.25f, (goal - shown) * 3f));
            if (ready && shown >= 0.999f && loadT >= minLoading)
            {
                if (fullAt < 0f) fullAt = now;
                if (now - fullAt >= FullHold)
                {
                    stage = Stage.Reveal;
                    stageAt = now;
                    toLogin = login;
                }
            }
            else fullAt = -1f;
            return;
        }
        if (stage == Stage.Reveal && now - stageAt >= RevealSeconds) stage = Stage.Done;
    }

    /// <summary>Город так долго не строится, что пора открыть как есть.</summary>
    public static bool CityTimedOut(float shownAt) { return Time.unscaledTime - shownAt > CityTimeout; }

    // ---------- Cinetica Games ----------

    static void DrawLogo(float t)
    {
        // Весь экран вместе с вырезом камеры: группа с началом в левом верхнем углу экрана
        Rect fr = UiKit.FullRect;
        GUI.BeginGroup(fr);
        try { DrawLogoIn(t, fr.width, fr.height); }
        finally { GUI.EndGroup(); }
    }

    static void DrawLogoIn(float t, float W, float H)
    {
        GUI.color = new Color(0.043f, 0.051f, 0.071f, 1f);
        GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);

        float outA = 1f - Smooth(Mathf.Clamp01((t - LogoBlack - LogoIn - LogoHold) / LogoOut));
        float te = t - LogoBlack;
        if (te <= 0f) return;

        float eh = Mathf.Clamp(H * 0.26f, 110f, 300f);   // высота знака
        float ew = emblem != null ? eh * emblem.width / emblem.height : eh;
        float wh = eh * 0.34f;                           // высота надписи
        float ww = wordmark != null ? wh * wordmark.width / wordmark.height : wh * 4.5f;
        if (ww > W * 0.8f) { float k = W * 0.8f / ww; ww *= k; wh *= k; }
        float gap = eh * 0.16f;
        float top = (H - eh - gap - wh) / 2f;

        // Знак: проявляется, доворачивается и чуть «пружинит»
        float e = Mathf.Clamp01(te / LogoIn);
        float scale = Mathf.LerpUnclamped(0.78f, 1f, BackOut(e));
        float angle = Mathf.Lerp(-35f, 0f, CubicOut(e));
        Vector2 c = new Vector2(W / 2f, top + eh / 2f);
        Matrix4x4 m = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), c);
        GUIUtility.RotateAroundPivot(angle, c);
        GUI.color = new Color(1f, 1f, 1f, Smooth(e) * outA);
        if (emblem != null) GUI.DrawTexture(new Rect(c.x - ew / 2f, top, ew, eh), emblem, ScaleMode.StretchToFill, true);
        GUI.matrix = m;

        // Надпись CINETICA GAMES: всплывает снизу
        float w = Mathf.Clamp01((te - WordDelay) / 0.7f);
        float dy = (1f - CubicOut(w)) * 14f;
        GUI.color = new Color(1f, 1f, 1f, Smooth(w) * outA);
        Rect wr = new Rect((W - ww) / 2f, top + eh + gap + dy, ww, wh);
        if (wordmark != null) GUI.DrawTexture(wr, wordmark, ScaleMode.StretchToFill, true);
        else
        {
            var st = new GUIStyle(UiKit.Title) { fontSize = Mathf.RoundToInt(wh * 0.6f), alignment = TextAnchor.MiddleCenter };
            GUI.Label(wr, "CINETICA GAMES", st);
        }
    }

    // ---------- Фон и название (общие с экраном входа) ----------

    /// <summary>Деревня во весь экран, медленно наезжает камера; сверху и снизу затемнение под надписи.
    /// dim - насколько ещё притушить (экран входа, связь потеряна).</summary>
    public static void DrawBackdrop(float alpha, float dim)
    {
        Load();
        Rect fr = UiKit.FullRect;   // весь экран вместе с вырезом камеры
        GUI.BeginGroup(fr);
        try { DrawBackdropIn(alpha, dim, fr.width, fr.height); }
        finally { GUI.EndGroup(); }
    }

    static void DrawBackdropIn(float alpha, float dim, float W, float H)
    {
        Color old = GUI.color;
        GUI.color = new Color(0.05f, 0.06f, 0.07f, alpha);
        GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
        Texture2D bg = WarLoading.On ? WarLoading.Background(StartupScreen.bg) : StartupScreen.bg;   // klan-voina-14: на войну - горящая деревня
        if (bg != null)
        {
            float t = Time.unscaledTime;
            float aspect = (float)bg.width / Mathf.Max(1, bg.height);
            float zoom = 1.06f + 0.05f * Mathf.Sin(t * 0.035f);
            float w, h;
            if (W / H > aspect) { w = W * zoom; h = w / aspect; }
            else { h = H * zoom; w = h * aspect; }
            float dx = (w - W) / 2f, dy = (h - H) / 2f;
            float ox = Mathf.Sin(t * 0.045f) * dx * 0.7f, oy = Mathf.Cos(t * 0.031f) * dy * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(-dx + ox, -dy + oy, w, h), bg, ScaleMode.StretchToFill, false);
        }
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(0f, 0f, W, H), shade, ScaleMode.StretchToFill, true);
        GUI.DrawTexture(new Rect(0f, 0f, W, H), vignette, ScaleMode.StretchToFill, true);
        if (WarLoading.On) WarLoading.DrawFire(W, H, alpha);   // klan-voina-14: зарево и искры
        if (dim > 0f)
        {
            GUI.color = new Color(0.02f, 0.025f, 0.035f, dim * alpha);
            GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
        }
        GUI.color = old;
    }

    // На низком экране телефона название поменьше - под ним должно уместиться окно входа
    static float TitleTop => Mathf.Max(UiKit.H < 520f ? 8f : 18f, UiKit.H * 0.05f);
    static float TitleWidth => Mathf.Min(UiKit.W * 0.7f, 860f, UiKit.H * (UiKit.H < 520f ? 0.62f : 1.25f));
    static float TitleHeight => title != null ? TitleWidth * title.height / title.width : Mathf.Clamp(UiKit.W / 11f, 40f, 84f) * 1.3f;
    /// <summary>Где кончается название игры с подписью (ниже - окна).</summary>
    public static float TitleBottom { get { Load(); return TitleTop + TitleHeight + 34f; } }

    /// <summary>Название STRATEGYCA и подпись под ним. Возвращает низ подписи.</summary>
    public static float DrawTitle(float alpha)
    {
        Load();
        return DrawTitleAt(alpha, 0f, UiKit.W, TitleTop, TitleWidth);
    }

    /// <summary>Название по центру области area (на телефоне экран входа: название слева, окно справа).</summary>
    public static float DrawTitle(float alpha, Rect area)
    {
        Load();
        float tw = Mathf.Min(area.width * 0.86f, 860f);
        float th = title != null ? tw * title.height / title.width : Mathf.Clamp(tw / 6f, 40f, 84f);
        if (th > area.height * 0.5f) { tw *= area.height * 0.5f / th; th = area.height * 0.5f; }
        float y = area.y + (area.height - th - 30f) * 0.45f;
        return DrawTitleAt(alpha, area.x, area.width, y, tw);
    }

    static float DrawTitleAt(float alpha, float x0, float W, float y, float tw)
    {
        Color old = GUI.color;
        float th = title != null ? tw * title.height / title.width : Mathf.Clamp(tw / 6f, 40f, 84f);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        Rect tr = new Rect(x0 + (W - tw) / 2f, y, tw, th);
        if (title != null) GUI.DrawTexture(tr, title, ScaleMode.StretchToFill, true);
        else
        {
            var st = new GUIStyle(UiKit.Title) { fontSize = Mathf.RoundToInt(th / 1.3f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
            st.normal.textColor = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, alpha);
            GUI.Label(tr, "STRATEGYCA", st);
        }
        GUI.color = old;
        var sub = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontSize = Mathf.RoundToInt(Mathf.Clamp(tw * 0.022f, 12f, 17f)) };
        // Тёмная полоса под подписью, чтобы она читалась на пёстрой деревне
        float bw = Mathf.Min(W, Mathf.Max(tw * 0.9f, 330f));
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(x0 + (W - bw) / 2f, tr.yMax + 1f, bw, 30f), band, ScaleMode.StretchToFill, true);
        GUI.color = old;
        ShadowLabel(new Rect(x0, tr.yMax + 4f, W, 24f), WarLoading.On ? WarLoading.Subtitle : "Стройте город  ·  собирайте клан  ·  воюйте за лигу", sub,
            WarLoading.On ? new Color(1f, 0.62f, 0.42f, alpha) : new Color(0.86f, 0.84f, 0.78f, alpha));   // klan-voina-14
        return tr.yMax + 30f;
    }

    // ---------- Полоска, что делаем, советы ----------

    static float BarWidth => Mathf.Min(560f, UiKit.W - 64f);
    static float BarTop => UiKit.H - Mathf.Max(UiKit.H < 520f ? 70f : 96f, UiKit.H * 0.15f);

    static void DrawProgress(string status, float alpha)
    {
        if (alpha <= 0f) return;
        float W = UiKit.W, H = UiKit.H;
        float bw = BarWidth, bh = 10f;
        Rect bar = new Rect((W - bw) / 2f, BarTop, bw, bh);
        Color old = GUI.color;

        // Что делаем и сколько готово
        var st = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleLeft, wordWrap = false, fontSize = 14 };
        var pct = new GUIStyle(st) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        ShadowLabel(new Rect(bar.x, bar.y - 26f, bar.width - 50f, 22f), status ?? "", st, new Color(0.95f, 0.93f, 0.88f, alpha));
        ShadowLabel(new Rect(bar.xMax - 60f, bar.y - 26f, 60f, 22f), Mathf.RoundToInt(shown * 100f) + "%", pct, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, alpha));

        // Рамка
        GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
        GUI.DrawTexture(new Rect(bar.x - 3f, bar.y - 3f, bar.width + 6f, bar.height + 6f), Texture2D.whiteTexture);
        GUI.color = new Color(0.78f, 0.60f, 0.30f, 0.9f * alpha);
        GUI.DrawTexture(new Rect(bar.x - 3f, bar.y - 3f, bar.width + 6f, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(bar.x - 3f, bar.yMax + 2f, bar.width + 6f, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(bar.x - 3f, bar.y - 3f, 1f, bar.height + 6f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(bar.xMax + 2f, bar.y - 3f, 1f, bar.height + 6f), Texture2D.whiteTexture);

        // Заполнение и бегущий блик
        float fw = bar.width * Mathf.Clamp01(shown);
        if (fw > 0.5f)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(bar.x, bar.y, fw, bar.height), WarLoading.On ? WarLoading.BarFill(barFill) : barFill, ScaleMode.StretchToFill, true);   // klan-voina-14
            float s = Mathf.Repeat(Time.unscaledTime * 0.7f, 1.6f) - 0.3f;   // блик бежит по всей полоске
            float sx = bar.x + bar.width * s, sw = bar.width * 0.12f;
            float a = Mathf.Max(bar.x, sx), b = Mathf.Min(bar.x + fw, sx + sw);
            if (b > a)
            {
                GUI.color = new Color(1f, 0.97f, 0.85f, 0.35f * alpha);
                GUI.DrawTexture(new Rect(a, bar.y, b - a, bar.height * 0.5f), Texture2D.whiteTexture);
            }
            // искра на конце
            GUI.color = new Color(1f, 0.92f, 0.65f, (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f)) * alpha);
            GUI.DrawTexture(new Rect(bar.x + fw - 2f, bar.y - 2f, 3f, bar.height + 4f), Texture2D.whiteTexture);
        }

        // Совет: меняется раз в несколько секунд, мягко
        float loadT = Time.unscaledTime - loadingAt;
        int i = (int)(loadT / TipSeconds);
        float ph = Mathf.Repeat(loadT, TipSeconds);
        float ta = Mathf.Clamp01(ph / 0.4f) * Mathf.Clamp01((TipSeconds - ph) / 0.4f);
        string[] tips = WarLoading.On ? WarLoading.Tips : Tips;   // klan-voina-14: советы про войну
        string tipText = tips[(tip + i) % tips.Length];
        var ts = new GUIStyle(UiKit.Small) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = 13, fontStyle = FontStyle.Italic };
        float tw = Mathf.Min(640f, W - 48f);
        ShadowLabel(new Rect((W - tw) / 2f, bar.yMax + 14f, tw, 40f), Loc.T("Совет") + ": " + Loc.T(tipText), ts, new Color(0.80f, 0.79f, 0.74f, alpha * ta));

        // Разработчик
        var cs = new GUIStyle(UiKit.Small) { alignment = TextAnchor.LowerRight, wordWrap = false, fontSize = 11 };
        ShadowLabel(new Rect(W - 260f, H - 26f, 248f, 18f), "© Cinetica Games", cs, new Color(0.75f, 0.74f, 0.70f, 0.7f * alpha));
        GUI.color = old;
    }

    static readonly string[] Tips =
    {
        "Город сохраняется на сервере сам: раз в полминуты и после каждой постройки",
        "Хранилища поднимают предел ресурсов. Казна упёрлась в потолок - пора строить ещё одно",
        "Руины после набега отстраиваются сами, но на восстановление уходят дерево и камень",
        "Пока идёт набег, улучшать здания нельзя. Готовьте оборону заранее",
        "Курьеры в дальних рейсах едят больше. Ставьте Курьерскую ближе к работе",
        "Аванпост Ур.4 сам отстреливается от врагов, хоть и слабее башни",
        "Дороги и брусчатка соединяются сами: просто стройте их рядом",
        "В окне клана есть вкладка «Лиги»: там ваш рейтинг и рейтинг клана",
        "Пока идёт клановая война, на кнопке «Клан» горит ⚔",
        "Ловушки собирают в Инженерном дворе: основа и добавки меняют, как они работают",
        "Отряд из лагеря можно отправить дальше кнопкой «Дальше...». Двойной клик - марш без остановок",
        "Пропала связь с сервером - игра встанет на паузу и продолжится сама, когда он вернётся",
    };

    // ---------- Картинки ----------

    static void Load()
    {
        if (loaded && shade != null) return;
        loaded = true;
        bg = Resources.Load<Texture2D>("Startup/loading_bg");
        title = Resources.Load<Texture2D>("Startup/title_strategyca");
        if (stage == Stage.Logo)
        {
            emblem = Resources.Load<Texture2D>("Startup/cinetica_emblem");
            wordmark = Resources.Load<Texture2D>("Startup/cinetica_wordmark");
        }

        // Затемнение: сверху под название, снизу под полоску, посередине чуть-чуть
        shade = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int i = 0; i < 128; i++)
        {
            float v = i / 127f;   // 0 - низ экрана
            float top = Mathf.Pow(Mathf.Clamp01((v - 0.48f) / 0.52f), 1.2f) * 0.85f;
            float bot = Mathf.Pow(Mathf.Clamp01((0.42f - v) / 0.42f), 1.3f) * 0.88f;
            shade.SetPixel(0, i, new Color(0.02f, 0.025f, 0.04f, Mathf.Clamp01(0.12f + top + bot)));
        }
        shade.Apply();

        vignette = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                vignette.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.Clamp01((d - 0.45f) / 0.55f) * 0.65f));
            }
        vignette.Apply();

        // Золото полоски: светлое сверху, тёмное снизу
        barFill = new Texture2D(1, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int i = 0; i < 16; i++)
        {
            float v = i / 15f;
            barFill.SetPixel(0, i, Color.Lerp(new Color(0.72f, 0.42f, 0.12f), new Color(1f, 0.86f, 0.48f), v));
        }
        barFill.Apply();

        // Полоса под подписью: тёмная в середине, к краям тает
        band = new Texture2D(64, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 64; x++)
            {
                float a = Mathf.Sin(x / 63f * Mathf.PI) * Mathf.Sin((y + 0.5f) / 8f * Mathf.PI);
                band.SetPixel(x, y, new Color(0.02f, 0.025f, 0.04f, 0.7f * Mathf.Pow(a, 0.7f)));
            }
        band.Apply();
    }

    /// <summary>Заставка больше не нужна - отпускаем её картинки.</summary>
    static void UnloadLogo()
    {
        if (emblem != null) Resources.UnloadAsset(emblem);
        if (wordmark != null) Resources.UnloadAsset(wordmark);
        emblem = wordmark = null;
    }

    // ---------- Мелочи ----------

    static void ShadowLabel(Rect r, string text, GUIStyle st, Color c)
    {
        if (c.a <= 0.01f) return;
        string s = Loc.T(text);
        var sh = new GUIStyle(st) { richText = false };
        sh.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.8f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1.5f, r.width, r.height), s, sh);
        sh.normal.textColor = c;
        GUI.Label(r, s, sh);
    }

    static float Smooth(float t) { return t * t * (3f - 2f * t); }
    static float CubicOut(float t) { t = 1f - t; return 1f - t * t * t; }
    static float BackOut(float t) { const float s = 1.6f; t -= 1f; return t * t * ((s + 1f) * t + s) + 1f; }
}
