using UnityEngine;

/// <summary>
/// Настройки игрока (хранятся в PlayerPrefs, общие для всех аккаунтов на этом компьютере):
///  - язык интерфейса (Loc);
///  - громкость: общая (сразу ставится в AudioListener), музыка, звуки, окружение (GameAudio); тишина, когда игра свёрнута;
///  - переспрашивать перед важными действиями (ConfirmDialog);
///  - всплывающие подсказки UiKit.Tip;
///  - облака, птицы, светлячки и эффекты построек (AmbientLife, ArtBuildingFx);
///  - пройти обучение заново (Tutorial).
/// Окно настроек открывается шестерёнкой в правом верхнем углу (рисует AccountManager), вкладка «Общие» - здесь.
/// </summary>
public static class GameSettings
{
    const string KMaster = "vol_master", KMusic = "vol_music", KSfx = "vol_sfx", KAmb = "vol_amb", KMuteBg = "mute_bg", KConfirm = "ask_confirm", KTips = "show_tips", KAmbient = "ambient_fx", KUiSize = "ui_size", KMinor = "minor_notes";

    static bool loaded;
    static float master = 0.8f, music = 0.7f, sfx = 0.8f, amb = 0.7f;
    static bool confirm = true, tips = true, ambient = true, muteBg = true, minorNotes = true;
    static float uiSize = 1f;
    /// <summary>Шаги настройки «Размер интерфейса».</summary>
    static readonly float[] UiSizes = { 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.35f, 1.5f, 1.75f, 2f };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { loaded = false; Load(); }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        master = PlayerPrefs.GetFloat(KMaster, 0.8f);
        music = PlayerPrefs.GetFloat(KMusic, 0.7f);
        sfx = PlayerPrefs.GetFloat(KSfx, 0.8f);
        amb = PlayerPrefs.GetFloat(KAmb, 0.7f);
        muteBg = PlayerPrefs.GetInt(KMuteBg, 1) == 1;
        confirm = PlayerPrefs.GetInt(KConfirm, 1) == 1;
        tips = PlayerPrefs.GetInt(KTips, 1) == 1;
        ambient = PlayerPrefs.GetInt(KAmbient, 1) == 1;
        minorNotes = PlayerPrefs.GetInt(KMinor, 1) == 1;
        uiSize = Mathf.Clamp(PlayerPrefs.GetFloat(KUiSize, 1f), UiSizes[0], UiSizes[UiSizes.Length - 1]);
        AudioListener.volume = master;
    }

    /// <summary>Общая громкость 0..1.</summary>
    public static float Master { get { Load(); return master; } }
    /// <summary>Громкость музыки 0..1 (уже умножена на общую).</summary>
    public static float Music { get { Load(); return music * master; } }
    /// <summary>Громкость звуков 0..1 (уже умножена на общую).</summary>
    public static float Effects { get { Load(); return sfx * master; } }
    // Общую громкость уже применяет AudioListener, поэтому GameAudio берёт свои ползунки без неё
    /// <summary>Ползунок «Музыка» 0..1 (без общей).</summary>
    public static float MusicLevel { get { Load(); return music; } }
    /// <summary>Ползунок «Звуки» 0..1 (без общей).</summary>
    public static float EffectsLevel { get { Load(); return sfx; } }
    /// <summary>Ползунок «Окружение» 0..1 (без общей): ветер, птицы, сверчки, шум боя вдали.</summary>
    public static float AmbienceLevel { get { Load(); return amb; } }
    /// <summary>Глушить звук, когда игра свёрнута или окно не в фокусе.</summary>
    public static bool MuteInBackground { get { Load(); return muteBg; } }
    /// <summary>Переспрашивать перед важными действиями.</summary>
    public static bool Confirm { get { Load(); return confirm; } }
    /// <summary>Показывать всплывающие подсказки.</summary>
    public static bool Tips { get { Load(); return tips; } }
    /// <summary>Облака, птицы, светлячки, свет в окнах и эффекты построек.</summary>
    public static bool Ambient { get { Load(); return ambient; } }
    /// <summary>«Размер интерфейса»: во сколько раз крупнее окна и кнопки (1 - обычный; на телефоне обычный уже под палец).</summary>
    public static float UiSize { get { Load(); return uiSize; } }
    /// <summary>final-13-fix-2: показывать мелкие вести (склад, кухня, производство) маленькой плашкой. Выключено - только журнал.</summary>
    public static bool MinorNotes { get { Load(); return minorNotes; } }

    static void Save()
    {
        PlayerPrefs.SetFloat(KMaster, master);
        PlayerPrefs.SetFloat(KMusic, music);
        PlayerPrefs.SetFloat(KSfx, sfx);
        PlayerPrefs.SetFloat(KAmb, amb);
        PlayerPrefs.SetInt(KMuteBg, muteBg ? 1 : 0);
        PlayerPrefs.SetInt(KConfirm, confirm ? 1 : 0);
        PlayerPrefs.SetInt(KTips, tips ? 1 : 0);
        PlayerPrefs.SetInt(KAmbient, ambient ? 1 : 0);
        PlayerPrefs.SetInt(KMinor, minorNotes ? 1 : 0);
        PlayerPrefs.SetFloat(KUiSize, uiSize);
        PlayerPrefs.Save();
    }

    // ---------- Вкладка «Общие» ----------

    const float SliderRowH = 34f;

    public static float GeneralHeight(float w)
    {
        return UiKit.SectionH + 40f + 8f                // язык
            + UiKit.SectionH + SliderRowH * 4f + 26f     // звук
            + UiKit.SectionH + 26f * 4f + SizeRowH       // интерфейс
            + (UiKit.Phone ? 26f + UiKit.BtnH + 6f : 0f) // экономия батареи и жесты
            + 6f + UiKit.BtnH;                           // обучение
    }

    public static void DrawGeneral(Rect r)
    {
        Load();
        float x = r.x, y = r.y, w = r.width;

        // Язык
        y = UiKit.Section(x, y, w, "Язык");
        int n = Loc.LangNames.Length;
        float bw = (w - (n - 1) * 6f) / n;
        for (int i = 0; i < n; i++)
        {
            Rect b = new Rect(x + i * (bw + 6f), y, bw, 40f);
            bool on = (int)Loc.Current == i;
            // Имя языка пишем на нём самом - не переводим
            if (GUI.Button(b, "", UiKit.ButtonStyle(on ? UiKit.Kind.Primary : UiKit.Kind.Normal))) Loc.Set((Loc.Lang)i);
            DrawFlag(new Rect(b.x + 8f, b.y + 12f, 24f, 16f), i);
            var st = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleLeft, wordWrap = false, fontStyle = on ? FontStyle.Bold : FontStyle.Normal };
            GUI.Label(new Rect(b.x + 38f, b.y, b.width - 40f, b.height), Loc.LangNames[i], st);
        }
        y += 40f + 8f;

        // Звук
        y = UiKit.Section(x, y, w, "Звук");
        float nm, nmu, ns, nam;
        nm = Slider(new Rect(x, y, w, SliderRowH), "Общая громкость", master); y += SliderRowH;
        nmu = Slider(new Rect(x, y, w, SliderRowH), "Музыка", music); y += SliderRowH;
        ns = Slider(new Rect(x, y, w, SliderRowH), "Звуки", sfx); y += SliderRowH;
        nam = Slider(new Rect(x, y, w, SliderRowH), "Окружение", amb);
        UiKit.Tip(new Rect(x, y, w, SliderRowH), "Ветер и птицы днём, сверчки и сова ночью, далёкий шум битвы");
        y += SliderRowH;
        bool nmb = LocGUI.Toggle(new Rect(x, y, w, 24f), muteBg, " Тишина, когда игра свёрнута");
        y += 26f;
        if (nm != master || nmu != music || ns != sfx || nam != amb || nmb != muteBg)
        {
            bool sfxMoved = ns != sfx;
            master = nm; music = nmu; sfx = ns; amb = nam; muteBg = nmb;
            AudioListener.volume = master;
            if (!muteBg) AudioListener.pause = false;
            Save();
            if (sfxMoved) Sfx.Ui("ui_coins");   // сразу слышно, какая громкость
        }

        // Интерфейс
        y = UiKit.Section(x, y, w, "Интерфейс");
        bool nc = LocGUI.Toggle(new Rect(x, y, w, 24f), confirm, " Переспрашивать перед важными действиями");
        UiKit.Tip(new Rect(x, y, w, 24f), "Выгнать из клана, уволить бойцов, выйти из аккаунта, отменить поход — сначала появится вопрос «Точно?»");
        y += 26f;
        bool nt = LocGUI.Toggle(new Rect(x, y, w, 24f), tips, " Всплывающие подсказки");
        y += 26f;
        bool na = LocGUI.Toggle(new Rect(x, y, w, 24f), ambient, " Облака, птицы и светлячки");
        UiKit.Tip(new Rect(x, y, w, 24f), "Живая природа на карте, свет в окнах ночью, пыль и искры при постройке. На слабом компьютере можно выключить.");
        y += 26f;
        bool nmn = LocGUI.Toggle(new Rect(x, y, w, 24f), minorNotes, " Мелкие уведомления (склад, кухня, производство)");
        UiKit.Tip(new Rect(x, y, w, 24f), "Будничные вести — маленькой плашкой и без звука. Выключите — они будут только в журнале (⚙ → Журнал). О нападениях и войне игра сообщает всегда.");
        y += 26f;
        if (nc != confirm || nt != tips || na != ambient || nmn != minorNotes) { confirm = nc; tips = nt; ambient = na; minorNotes = nmn; Save(); }
        DrawSizeRow(new Rect(x, y, w, SizeRowH));
        y += SizeRowH;
        if (UiKit.Phone)
        {
            bool bs = MobilePlatform.BatterySaver;
            bool nbs = LocGUI.Toggle(new Rect(x, y, w, 24f), bs, " Экономия батареи");
            UiKit.Tip(new Rect(x, y, w, 24f), "Когда вы не трогаете экран и нет боя — 30 кадров в секунду вместо 60, и экран может погаснуть, как обычно. Во время войны и набега — всегда 60 кадров и экран не гаснет.");
            if (nbs != bs) MobilePlatform.BatterySaver = nbs;
            y += 26f;
            if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), "Управление пальцами и жесты", UiKit.Kind.Normal, null)) TouchControls.ShowHelp();
            y += UiKit.BtnH + 6f;
        }

        y += 6f;
        if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), "Пройти обучение заново", UiKit.Kind.Normal, "Короткое обучение для новичков: камера, Цитадель, казна, первая постройка")) Tutorial.Restart();
    }

    const float SizeRowH = 38f;

    // «Размер интерфейса   [−] 100% [+]» - кнопками, а не ползунком: от ползунка окно менялось бы прямо под пальцем
    static void DrawSizeRow(Rect r)
    {
        LocGUI.Label(new Rect(r.x, r.y + 6f, r.width - 150f, 22f), "Размер интерфейса", UiKit.Body);
        UiKit.Tip(new Rect(r.x, r.y, r.width - 150f, r.height), "Насколько крупно рисуются окна, кнопки и надписи. На телефоне обычный размер уже рассчитан под палец; если что-то не влезает в экран — уменьшите. Нажмите на число — вернётся 100%.");
        int cur = 0;
        for (int i = 0; i < UiSizes.Length; i++) if (Mathf.Abs(UiSizes[i] - uiSize) < Mathf.Abs(UiSizes[cur] - uiSize)) cur = i;
        float bw = 40f, bx = r.xMax - bw * 2f - 70f;
        if (UiKit.Button(new Rect(bx, r.y + 2f, bw, r.height - 6f), "−", cur > 0, UiKit.Kind.Normal, null)) { uiSize = UiSizes[cur - 1]; Save(); }
        var mid = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontStyle = FontStyle.Bold };
        // Нажать на число - вернуть обычный размер (100%)
        if (GUI.Button(new Rect(bx + bw, r.y + 2f, 70f, r.height - 6f), Mathf.RoundToInt(UiSizes[cur] * 100f) + "%", mid) && uiSize != 1f) { uiSize = 1f; Save(); }
        if (UiKit.Button(new Rect(bx + bw + 70f, r.y + 2f, bw, r.height - 6f), "+", cur < UiSizes.Length - 1, UiKit.Kind.Normal, null)) { uiSize = UiSizes[cur + 1]; Save(); }
    }

    static float Slider(Rect r, string label, float v)
    {
        LocGUI.Label(new Rect(r.x, r.y, r.width - 50f, 18f), label, UiKit.Body);
        var right = new GUIStyle(UiKit.Body) { alignment = TextAnchor.UpperRight, wordWrap = false };
        GUI.Label(new Rect(r.x, r.y, r.width, 18f), Mathf.RoundToInt(v * 100f) + "%", right);
        float nv = GUI.HorizontalSlider(new Rect(r.x, r.y + 19f, r.width, 12f), v, 0f, 1f);
        return Mathf.Abs(nv - v) < 0.001f ? v : Mathf.Round(nv * 100f) / 100f;
    }

    // Маленький флаг языка
    static void DrawFlag(Rect r, int lang)
    {
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), Texture2D.whiteTexture);
        Color[] stripes;
        switch (lang)
        {
            case 0: stripes = new[] { Color.white, new Color(0.0f, 0.22f, 0.65f), new Color(0.84f, 0.17f, 0.12f) }; break;        // RU
            case 2: stripes = new[] { new Color(0.99f, 0.73f, 0.07f), new Color(0.0f, 0.42f, 0.27f), new Color(0.76f, 0.15f, 0.18f) }; break;   // LT
            default: stripes = null; break;
        }
        if (stripes != null)
        {
            float h = r.height / 3f;
            for (int i = 0; i < 3; i++)
            {
                GUI.color = stripes[i];
                GUI.DrawTexture(new Rect(r.x, r.y + i * h, r.width, h + 0.5f), Texture2D.whiteTexture);
            }
        }
        else
        {
            // EN: Юнион Джек (прямой крест и диагонали) - картинкой, полосками косые линии не нарисовать
            GUI.color = Color.white;
            GUI.DrawTexture(r, UnionJack, ScaleMode.StretchToFill);
        }
        GUI.color = old;
    }

    static Texture2D unionJack;

    /// <summary>
    /// Флаг Великобритании 120×60, нарисованный в коде по настоящим пропорциям (флаг 60×30):
    /// синее поле; белые диагонали шириной 6 и на них красные шириной 2, сдвинутые «вертушкой»
    /// (у древка сверху широкая белая полоса над красной); прямой белый крест 10 и красный 6.
    /// </summary>
    static Texture2D UnionJack
    {
        get
        {
            if (unionJack != null) return unionJack;
            const int W = 120, H = 60, Sub = 3;
            Color32 blue = new Color32(1, 33, 105, 255), white = new Color32(255, 255, 255, 255), red = new Color32(200, 16, 46, 255);
            unionJack = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UnionJack" };
            var px = new Color32[W * H];
            for (int py = 0; py < H; py++)
                for (int pxi = 0; pxi < W; pxi++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (int sy = 0; sy < Sub; sy++)
                        for (int sx = 0; sx < Sub; sx++)
                        {
                            // Координаты флага 60×30, y вниз (строка 0 текстуры - низ флага)
                            float x = (pxi + (sx + 0.5f) / Sub) / W * 60f;
                            float y = 30f - (py + (sy + 0.5f) / Sub) / H * 30f;
                            Color32 c = UnionJackAt(x, y, blue, white, red);
                            r += c.r; g += c.g; b += c.b;
                        }
                    const float n = Sub * Sub;
                    px[py * W + pxi] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            unionJack.SetPixels32(px);
            unionJack.Apply();
            return unionJack;
        }
    }

    static Color32 UnionJackAt(float x, float y, Color32 blue, Color32 white, Color32 red)
    {
        float dx = x - 30f, dy = y - 15f;
        // Прямой крест св. Георгия поверх всего
        if (Mathf.Abs(dx) < 3f || Mathf.Abs(dy) < 3f) return red;
        if (Mathf.Abs(dx) < 5f || Mathf.Abs(dy) < 5f) return white;
        // Диагональ своей четверти: из угла флага в центр. Направление от центра к углу:
        float ux = dx < 0f ? -2f : 2f, uy = dy < 0f ? -1f : 1f;
        float len = Mathf.Sqrt(ux * ux + uy * uy);
        ux /= len; uy /= len;
        // Расстояние от диагонали со знаком; красная полоса лежит по одну сторону - по «вертушке»:
        // сторона = направление к углу, повёрнутое (x, y) -> (y, -x) (y вниз). У древка сверху красная ниже белой.
        float s = dx * uy - dy * ux;    // проекция на (uy, -ux)
        if (s > 0f && s < 2f) return red;
        if (Mathf.Abs(s) < 3f) return white;
        return blue;
    }

    // ---------- Значок шестерёнки ----------

    static Texture2D gear;

    /// <summary>Шестерёнка 64×64, нарисованная в коде (белая - красится через GUI.color).</summary>
    public static Texture2D GearIcon
    {
        get
        {
            if (gear != null) return gear;
            const int S = 64;
            gear = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GearIcon" };
            var px = new Color32[S * S];
            const int teeth = 8;
            for (int yy = 0; yy < S; yy++)
                for (int xx = 0; xx < S; xx++)
                {
                    // 4×4 подвыборки на точку - гладкий край
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float fx = (xx + (sx + 0.5f) / 4f) - S * 0.5f, fy = (yy + (sy + 0.5f) / 4f) - S * 0.5f;
                            float d = Mathf.Sqrt(fx * fx + fy * fy) / (S * 0.5f);
                            float a = Mathf.Atan2(fy, fx);
                            // Зубец: трапеция по углу
                            float t = Mathf.Repeat(a / (Mathf.PI * 2f) * teeth, 1f);
                            float tooth = Mathf.Clamp01(1f - Mathf.Abs(t - 0.5f) * 4f + 0.6f);
                            float outer = Mathf.Lerp(0.72f, 0.97f, Mathf.Clamp01(tooth));
                            if (d <= outer && d >= 0.30f) hit++;
                        }
                    byte al = (byte)(255 * hit / 16);
                    px[yy * S + xx] = new Color32(255, 255, 255, al);
                }
            gear.SetPixels32(px);
            gear.Apply();
            return gear;
        }
    }
}
