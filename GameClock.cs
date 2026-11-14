using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Игровые часы: день (20 мин) + ночь (10 мин), неделя из 7 дней. Выходных у рабочих нет (обновление 22): работают каждый день.
/// Показывает время и ману в маленькой плашке наверху справа, затемняет карту ночью и управляет скоростью игры.
/// Клавиши: F1 - пауза, F2 - x1, F3 - x2, F4 - x4, F5 - x10, F10 - спрятать / показать панель.
/// В онлайне время общее для всех (WorldClock): скорости и пауза отключены, сутки идут по часам сервера.
/// </summary>
public class GameClock : MonoBehaviour
{
    public static GameClock Instance { get; private set; }

    [Header("Длительность (реальные минуты при скорости x1)")]
    public float dayMinutes = 20f;
    public float nightMinutes = 10f;

    [Header("Неделя")]
    public int daysPerWeek = 7;
    public int workDaysPerWeek = 5;

    [Header("Ночная смена и выходные")]
    public int skeletonCrew = 1;       // сколько человек остаётся на производстве ночью и в выходные
    public int skeletonCrewBig = 2;    // сколько остаётся на большом здании (от bigBuildingLevel)
    public int bigBuildingLevel = 3;

    [Header("Затемнение ночью")]
    public float nightDarkness = 0.45f;
    public float twilightSeconds = 60f;

    static readonly string[] DayNames = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
    static readonly float[] Speeds = { 0f, 1f, 2f, 4f, 10f };
    static readonly string[] SpeedLabels = { "II", "x1", "x2", "x4", "x10" };

    private float timeInDay = 0f;   // секунд с начала суток (сутки начинаются с утра, 06:00)
    private int dayIndex = 0;       // сколько суток прошло (0 = первый понедельник)
    private int speedIndex = 1;
    private int lastSpeedIndex = 1;
    private SpriteRenderer overlay;
    private bool hidden = false;   // F10 - спрятать плашку (часы, мана, скорость)

    public bool IsHidden => hidden;

    float DayLen => dayMinutes * 60f;
    float NightLen => nightMinutes * 60f;
    float TotalLen => DayLen + NightLen;

    public bool IsNight => timeInDay >= DayLen;
    public int DayIndex => dayIndex;
    public float TimeInDay => timeInDay;

    /// <summary>Время из сохранённого города.</summary>
    public void RestoreTime(int day, float t)
    {
        if (WorldClock.Active) return;   // в онлайне время общее, сохранённое не нужно
        dayIndex = Mathf.Max(0, day);
        timeInDay = Mathf.Clamp(t, 0f, TotalLen - 0.01f);
    }
    public int DayOfWeekIndex => dayIndex % Mathf.Max(1, daysPerWeek);
    // Обновление 22: выходных нет, жители работают каждый день (отдыхают только ночью).
    // Чтобы вернуть выходные, поставьте RestOnWeekends = true.
    public static readonly bool RestOnWeekends = false;
    public bool IsWeekend => RestOnWeekends && DayOfWeekIndex >= workDaysPerWeek;
    int WorkDays => RestOnWeekends ? workDaysPerWeek : daysPerWeek;

    // Сколько человек дежурит в здании данного уровня, когда не рабочее время (остальные идут домой)
    public int NightCrewFor(int level)
    {
        return level >= bigBuildingLevel ? skeletonCrewBig : skeletonCrew;
    }

    // Рабочее время: день (выходных нет). В режиме накопления все работают и ночью (обновление 21)
    public bool IsWorkTime => (!IsNight && !IsWeekend) || AccumulationMode.On;

    /// <summary>Доля недели, когда работают все (будние дни, днём).</summary>
    public float WorkShare => DayLen / Mathf.Max(1f, TotalLen) * Mathf.Clamp01(WorkDays / (float)Mathf.Max(1, daysPerWeek));

    /// <summary>Какая доля бригады из workers человек работает в среднем за неделю (днём все, в остальное время - дежурные).</summary>
    public float AverageShift(int workers, int level)
    {
        if (workers <= 0 || AccumulationMode.On) return 1f;   // режим накопления: без ночи и выходных
        float crew = Mathf.Min(workers, NightCrewFor(level)) / (float)workers;
        return WorkShare + (1f - WorkShare) * crew;
    }

    /// <summary>Какая доля бригады из workers человек работает прямо сейчас.</summary>
    public float ShiftNow(int workers, int level)
    {
        if (IsWorkTime || workers <= 0) return 1f;
        return Mathf.Min(workers, NightCrewFor(level)) / (float)workers;
    }

    // ---------- Ночь и выходные: игроку должно быть видно, почему ресурсы копятся медленнее ----------
    // На плашке с часами - «ночь · работа 35%», в окне здания - «На месте 1/4», при входе и на смене дня - сообщение.

    /// <summary>Сколько игровых секунд до начала рабочего времени (0 - уже работают).</summary>
    public float SecondsToWork
    {
        get
        {
            if (IsWorkTime) return 0f;
            int days = Mathf.Max(1, daysPerWeek);
            float s = 0f, t = timeInDay;
            int d = dayIndex;
            if (t >= DayLen) { s += TotalLen - t; d++; t = 0f; }   // ночь - до утра
            for (int i = 0; i < days && d % days >= WorkDays; i++) { s += TotalLen - t; d++; t = 0f; }   // выходные - до понедельника
            return s;
        }
    }

    float shiftPct = 1f, shiftPctAt = -10f;

    /// <summary>Какая доля рабочих всего города сейчас на местах (1 днём; ночью и в выходные - дежурные). -1: рабочих нет.</summary>
    public float CityShift
    {
        get
        {
            if (IsWorkTime) return 1f;
            if (Time.unscaledTime - shiftPctAt < 1f) return shiftPct;
            shiftPctAt = Time.unscaledTime;
            int all = 0, on = 0;
            foreach (var b in BuildingInstance.All)
            {
                if (b == null || b.workers <= 0 || b.workersNeeded <= 0 || b.IsRuined) continue;
                all += b.workers;
                on += Mathf.Min(b.workers, NightCrewFor(b.level));
            }
            shiftPct = all > 0 ? on / (float)all : -1f;
            return shiftPct;
        }
    }

    /// <summary>Подпись на плашке: «ночь · работа 35%» (пусто днём в будни).</summary>
    public string ShiftBadge
    {
        get
        {
            if (IsWorkTime) return "";
            float k = CityShift;
            string what = IsWeekend ? "выходной" : "ночь";
            return k >= 0f ? $"{what} · работа {Mathf.RoundToInt(k * 100f)}%" : what;
        }
    }

    static string Minutes(float sec)
    {
        int m = Mathf.Max(1, Mathf.CeilToInt(sec / 60f));
        return m >= 60 ? $"{m / 60} ч {m % 60:00} мин" : $"{m} мин";
    }

    /// <summary>Почему сейчас всё медленнее и когда пройдёт. Пусто днём в будни.</summary>
    public string ShiftExplain
    {
        get
        {
            if (IsWorkTime) return "";
            float k = CityShift;
            string head = IsWeekend ? "Выходной: рабочие отдыхают дома." : "Ночь: рабочие спят дома.";
            string crew = skeletonCrew == skeletonCrewBig ? $"{skeletonCrew}" : $"{skeletonCrew}–{skeletonCrewBig}";
            string body = $"В зданиях осталась только дежурная смена ({crew} чел.), поэтому ресурсы копятся медленнее";
            if (k >= 0f) body += $": сейчас работает {Mathf.RoundToInt(k * 100f)}% рабочих.";
            else body += ".";
            float wait = SecondsToWork;
            string when = (wait > NightLen + 1f ? "Все выйдут на работу в понедельник утром, через " : "Все выйдут на работу утром (06:00), через ") + Minutes(wait) + ".";
            return head + " " + body + "\n" + when;
        }
    }

    int shiftState = -1;          // -1 - ещё не смотрели (только что вошли), 0 - нерабочее время, 1 - рабочее
    float worldReadyAt = -1f;

    // Смена дня и ночи (и выходных) - сообщение игроку. При входе ночью - тоже, чтобы не гадал, почему всё медленно
    void CheckShift()
    {
        var bp = BuildingPlacer.Instance;
        var pm = PopulationManager.Instance;
        if (bp == null || !bp.WorldReady || pm == null || pm.Hall == null) { shiftState = -1; worldReadyAt = -1f; return; }
        if (worldReadyAt < 0f) worldReadyAt = Time.unscaledTime;
        if (Time.unscaledTime - worldReadyAt < 4f) return;   // сначала - сводка «Пока вас не было»
        int now = IsWorkTime ? 1 : 0;
        if (now == shiftState) return;
        bool first = shiftState < 0;
        shiftState = now;
        if (now == 0) { shiftPctAt = -10f; WarManager.Notify(ShiftExplain.Replace("\n", " "), true); }
        else if (!first && AccumulationMode.On && (IsNight || IsWeekend))
            WarManager.Notify("Режим накопления: все рабочие на местах и ночью", true);
        else if (!first) WarManager.Notify("Утро: рабочие вернулись в здания, добыча снова в полную силу", true);
    }

    // Игровые часы 0..24 (сутки начинаются в 06:00, день идёт до 22:00, потом ночь)
    public float Hours => (6f + timeInDay / TotalLen * 24f) % 24f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        CreateOverlay();
    }

    void OnDestroy()
    {
        if (Instance == this) Time.timeScale = 1f;
    }

    void CreateOverlay()
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        GameObject go = new GameObject("NightOverlay");
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(400f, 400f, 1f);

        overlay = go.AddComponent<SpriteRenderer>();
        overlay.sprite = sp;
        overlay.sortingOrder = 50;
        overlay.color = new Color(0.04f, 0.06f, 0.18f, 0f);
    }

    void SetSpeed(int index)
    {
        speedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);
        if (speedIndex > 0) lastSpeedIndex = speedIndex;
        Time.timeScale = Speeds[speedIndex];
    }

    /// <summary>Время общее (онлайн): скорость менять нельзя.</summary>
    public bool Shared => WorldClock.Active;

    void Update()
    {
        if (Shared)
        {
            // Скорость в онлайне всегда x1 (паузу на экране входа ставит AccountManager, её не трогаем)
            if (speedIndex != 1) SetSpeed(1);
            if (Time.timeScale > 1f) Time.timeScale = 1f;
            double total = WorldClock.Seconds;
            dayIndex = (int)(total / TotalLen);
            timeInDay = (float)(total - (double)dayIndex * TotalLen);
            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame) hidden = !hidden;
            UpdateOverlay();
            CheckShift();
            return;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.f1Key.wasPressedThisFrame) SetSpeed(speedIndex == 0 ? lastSpeedIndex : 0);
            if (Keyboard.current.f2Key.wasPressedThisFrame) SetSpeed(1);
            if (Keyboard.current.f3Key.wasPressedThisFrame) SetSpeed(2);
            if (Keyboard.current.f4Key.wasPressedThisFrame) SetSpeed(3);
            if (Keyboard.current.f5Key.wasPressedThisFrame) SetSpeed(4);
            if (Keyboard.current.f10Key.wasPressedThisFrame) hidden = !hidden;
        }

        timeInDay += Time.deltaTime;
        while (timeInDay >= TotalLen)
        {
            timeInDay -= TotalLen;
            dayIndex++;
        }

        UpdateOverlay();
        CheckShift();
    }

    void UpdateOverlay()
    {
        if (overlay == null) return;
        Color c = overlay.color;
        c.a = Darkness() * nightDarkness;
        overlay.color = c;
        FitOverlay();
    }

    // final-12-fix-1: ночь была квадратом 400x400 вокруг деревни, а на войне карта вокруг куда больше - вокруг деревни
    // виднелся тёмный квадрат. На войне ночь накрывает весь экран: идёт за камерой и тянется под её размер.
    void FitOverlay()
    {
        Transform t = overlay.transform;
        Camera cam = WarCityMode.Active ? Camera.main : null;
        if (cam == null || !cam.orthographic)
        {
            if (t.localPosition != Vector3.zero) { t.localPosition = Vector3.zero; t.localScale = new Vector3(400f, 400f, 1f); }
            return;
        }
        float h = cam.orthographicSize * 2f, w = h * cam.aspect;
        float s = Mathf.Max(400f, Mathf.Max(w, h) * 1.6f);   // с запасом: камера могла сдвинуться после нас в этом кадре
        Vector3 p = cam.transform.position;
        t.position = new Vector3(p.x, p.y, t.position.z);
        t.localScale = new Vector3(s, s, 1f);
    }

    // 0 = светло, 1 = полная ночь. Плавный рассвет и закат.
    float Darkness()
    {
        float tw = Mathf.Min(twilightSeconds, DayLen * 0.5f);
        if (tw <= 0.01f) return IsNight ? 1f : 0f;

        if (IsNight) return 1f;
        if (timeInDay < tw) return 1f - timeInDay / tw;          // рассвет
        if (timeInDay > DayLen - tw) return (timeInDay - (DayLen - tw)) / tw; // закат
        return 0f;
    }

    // ---------- UI ----------
    // Компактная плашка наверху справа: «● Пн 14:32 · ✦ 64%» и (офлайн) пауза и скорость.
    // Встаёт в одну строку с казной, левее кнопок «Клан», «Путь», «Город», ⚙, если там хватает места;
    // иначе (телефон, узкое окно) - сразу под этими кнопками, у правого края. Внизу экрана больше ничего нет.
    // Под окнами не просвечивает: если её закрывает открытое окно, она не рисуется.
    // Тонкая полоска по низу плашки - мана в воздухе.
    const float SpeedBtnW = 34f, PauseBtnW = 30f;

    static float PillH => UiKit.Phone ? 30f : 26f;

    float lastTextW = 70f;   // ширина «Ср 00:00», измеренная в OnGUI
    float lastBadgeW = 0f;   // ширина «ночь · работа 35%» (0 - рабочее время)

    float PanelW
    {
        get
        {
            // Стили окон можно трогать только внутри OnGUI. Вне его (уведомление из ответа сервера и т.п.) - прошлая ширина
            if (UiKit.InGui)
            {
                lastTextW = TimeStyle().CalcSize(Loc.Content("Ср 00:00")).x;
                string badge = ShiftBadge;
                lastBadgeW = badge == "" ? 0f : BadgeStyle().CalcSize(Loc.Content(badge)).x + 8f;
            }
            float w = 10f + 12f + lastTextW + 10f + ManaW + 8f + lastBadgeW;
            if (!Shared) w += PauseBtnW + 3f + SpeedBtnW + 4f;
            return Mathf.Round(w);
        }
    }

    const float ManaW = 50f;

    /// <summary>Плашка стоит в строке казны (true) или под кнопками справа сверху (false).</summary>
    public bool InTopRow { get; private set; }

    public Rect PanelRect
    {
        get
        {
            float w = PanelW, h = PillH;
            float rowX = ResourceManager.TopButtonsLeft - 8f - w;
            InTopRow = rowX >= ResourceManager.HudRight + 8f;
            if (InTopRow) return new Rect(rowX, UiKit.TopBtnY + (UiKit.TopBtnH - h) * 0.5f, w, h);
            return new Rect(UiKit.W - w - (UiKit.Phone ? 6f : 10f), UiKit.TopBtnY + UiKit.TopBtnH + 4f, w, h);
        }
    }

    /// <summary>Левый край плашки, если она в строке казны (баннер войны и уведомления не должны на неё лечь); иначе - ширина экрана.</summary>
    public static float RowLeft
    {
        get
        {
            var c = Instance;
            if (c == null || c.hidden || WarView.IsOpen) return UiKit.W;
            Rect r = c.PanelRect;
            return c.InTopRow ? r.x : UiKit.W;
        }
    }

    bool covered;   // плашку закрывает окно - не рисуем и не ловим нажатия

    public bool IsPointerOver(Vector2 guiPoint)
    {
        return !hidden && !covered && PanelRect.Contains(guiPoint);
    }

    static GUIStyle timeSt, manaSt, speedSt, badgeSt;
    static GUIStyle BadgeStyle()
    {
        if (badgeSt == null) badgeSt = new GUIStyle(UiKit.Body) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        return badgeSt;
    }
    static GUIStyle TimeStyle()
    {
        if (timeSt == null) timeSt = new GUIStyle(UiKit.Body) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        return timeSt;
    }

    void OnGUI()
    {
        if (WarView.IsOpen) return;   // открыта карта клановой войны
        UiKit.Apply();
        GUI.depth = 9;   // позади окон (казна - 10)
        if (hidden) return;
        Rect p = PanelRect;
        covered = UiKit.UnderWindow(p);
        if (covered) return;
        LocGUI.Box(p, GUIContent.none);

        // День и время, точка - день (золотая) или ночь (голубая)
        int h = Mathf.FloorToInt(Hours);
        int m = Mathf.FloorToInt((Hours - h) * 60f);
        string day = DayNames[DayOfWeekIndex % DayNames.Length];
        GUIStyle ts = TimeStyle();
        ts.normal.textColor = IsNight ? UiKit.Info : UiKit.Text;
        float x = p.x + 10f;
        string dot = UiKit.Col(IsNight ? UiKit.InfoHex : UiKit.AccentHex, "●");
        string dayText = IsWeekend ? UiKit.Col(UiKit.MutedHex, day) : day;
        Rect timeR = new Rect(x, p.y, ts.CalcSize(Loc.Content("Ср 00:00")).x + 14f, p.height - 2f);
        LocGUI.Label(timeR, $"{dot} {dayText} {h:00}:{m:00}", ts);
        x = timeR.xMax + 4f;

        // Ночь или выходной: сразу видно, что работают только дежурные и насколько это медленнее
        string badge = ShiftBadge;
        Rect badgeR = new Rect(x, p.y, 0f, p.height - 2f);
        if (badge != "")
        {
            badgeR.width = lastBadgeW;
            LocGUI.Label(badgeR, UiKit.Col(UiKit.WarnHex, badge), BadgeStyle());
            x = badgeR.xMax;
        }

        // Мана: «✦ 64%»
        var mana = ManaManager.Instance;
        int pct = mana != null ? mana.Percent : 100;
        if (manaSt == null) manaSt = new GUIStyle(UiKit.Body) { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        Rect manaR = new Rect(x, p.y, ManaW, p.height - 2f);
        LocGUI.Label(manaR, UiKit.Col("#8FB0FF", "✦") + " " + UiKit.Col("#C8D6FF", pct + "%"), manaSt);

        // Тонкая полоска маны по низу плашки
        float k = pct / 100f;
        Color fill = Color.Lerp(new Color(0.40f, 0.42f, 0.52f), new Color(0.40f, 0.55f, 0.95f), k);
        UiKit.Bar(new Rect(p.x + 8f, p.yMax - 5f, p.width - 16f, 3f), k, fill);

        string phase = IsNight ? "ночь" : "день";
        if (IsWeekend) phase += ", выходной";
        string shift = ShiftExplain;
        UiKit.Tip(new Rect(p.x, p.y, manaR.xMax - p.x, p.height),
            (shift != "" ? UiKit.B(UiKit.Col(UiKit.WarnHex, shift)) + "\n\n" : "") +
            $"День {dayIndex + 1}, сейчас {phase}. Сутки: день {dayMinutes:F0} мин + ночь {nightMinutes:F0} мин" + (Shared ? " (общее время сервера)." : " (при x1).") + "\n" +
            (RestOnWeekends ? $"Неделя: {workDaysPerWeek} рабочих дней и {daysPerWeek - workDaysPerWeek} выходных. Ночью и в выходные на производстве остаётся дежурная смена.\n"
                            : "Выходных нет: рабочие трудятся каждый день. Ночью на производстве остаётся дежурная смена.\n") +
            $"✦ Мана в воздухе {pct}% — новая каждый день. Маги и целители работают на x{ManaManager.Factor:F2}" +
            (mana != null ? $" (при 0% — x{mana.minEfficiency:F2}, при 100% — x{mana.maxEfficiency:F2})." : ".") +
            (UiKit.Phone ? "" : "\nF10 — спрятать плашку"));

        if (Shared) return;   // онлайн: время общее, скорость не меняется

        // Офлайн: пауза и скорость (нажатие - следующая скорость)
        if (speedSt == null) speedSt = new GUIStyle(UiKit.ButtonStyle(UiKit.Kind.Normal)) { fontSize = 12, padding = new RectOffset(2, 2, 2, 2), wordWrap = false };
        float bh = p.height - 8f;
        Rect pr = new Rect(p.xMax - 4f - SpeedBtnW - 3f - PauseBtnW, p.y + 3f, PauseBtnW, bh);
        bool paused = speedIndex == 0;
        if (UiKit.Button(pr, paused ? "▶" : "II", paused ? UiKit.Kind.Primary : UiKit.Kind.Normal, paused ? "Продолжить (F1)" : "Пауза (F1)"))
            SetSpeed(paused ? lastSpeedIndex : 0);
        Rect sr = new Rect(pr.xMax + 3f, p.y + 3f, SpeedBtnW, bh);
        int cur = paused ? lastSpeedIndex : speedIndex;
        if (LocGUI.Button(sr, SpeedLabels[cur], speedSt)) SetSpeed(cur >= Speeds.Length - 1 ? 1 : cur + 1);
        UiKit.Tip(sr, "Скорость игры: x1 → x2 → x4 → x10\nF2 — x1, F3 — x2, F4 — x4, F5 — x10");
    }
}
