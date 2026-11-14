using UnityEngine;

/// <summary>
/// Режим накопления (меню «Город»): город копит дерево, камень, золото и еду, ничего не тратя на остальное.
///  - Работают только добытчики: Лесопилка, Шахта, Ферма, Станция собирателей. Цитадель, Курьерская и аванпосты
///    тоже работают: без них добычу некому носить.
///  - Рабочие остальных зданий (мастерские, кухня, оружейная, лекари...) уходят домой и сидят там. Здания стоят
///    на паузе и ничего не потребляют.
///  - Обновление 22: ни одно здание ничего не тратит, и добытчики тоже - содержания нет совсем (CityConsumption).
///    Печи завода не топятся, кухня не варит, еду на аванпосты не возят, смолу поджигателям не выдают.
///  - Жители не голодают и не едят, сами не лечатся (ни дома, ни в казарме, ни у лекаря). Так город с нулём
///    любого ресурса может выбраться: ничего не уходит, всё только копится.
///  - Чтобы это не было бесплатным:
///      · добытчики работают вполсилы (ProduceMul);
///      · новые жители в город не приходят;
///      · переключать режим можно не чаще раза в ToggleCooldown секунд.
///  - Пока игрока нет, сырьё прибавляется по выработке добытчиков в режиме (CityIncome), а не по доходу
///    до включения режима: там камень обычно уходил в минус на мастерские и башни.
///  - Ночи в режиме нет: все рабочие добытчиков на местах круглые сутки (GameClock.IsWorkTime). Выходных нет вообще.
///  - Выключили режим - рабочие идут обратно на свои места (ручные настройки «- / +» не теряются).
/// Режим сохраняется вместе с городом (CitySave).
/// </summary>
public static class AccumulationMode
{
    public const float ProduceMul = 0.6f;        // добытчики в режиме накопления
    public const float ToggleCooldown = 120f;    // секунд между переключениями
    // Голод и еда в режиме выключены совсем (CitizenAgent.HungerTick / EatInside), множители оставлены на 1
    public static float HungerFactor => 1f;
    public static float FoodValue => 1f;


    public static bool On { get; private set; }

    /// <summary>Сколько секунд режим включён (для панели и сохранения).</summary>
    public static float OnSeconds => On ? onSecondsBase + (Time.unscaledTime - onSince) : 0f;

    static float onSince, onSecondsBase;
    static float lastToggle = -9999f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { On = false; onSince = 0f; onSecondsBase = 0f; lastToggle = -9999f; offerHiddenUntil = -1f; offerHiddenMask = 0; }

    public static float CooldownLeft => Mathf.Max(0f, ToggleCooldown - (Time.unscaledTime - lastToggle));

    public static void Toggle()
    {
        if (CooldownLeft > 0f) return;
        Set(!On);
        lastToggle = Time.unscaledTime;
        WarManager.Notify(On
            ? "Режим накопления: работают только добытчики, остальные рабочие идут домой. Расходов нет, жители не едят"
            : "Режим накопления выключен: рабочие возвращаются в здания", true);
        CityIncome.Restart();   // запасы пойдут иначе - доход меряем заново, уже в новом режиме
    }

    // ---------- Кнопка наверху: «Кончилась еда — включить режим накопления» (обновление 22) ----------
    // Раньше режим прятался в меню «Город», и о нём надо было догадаться. Теперь, когда в казне ноль золота, дерева,
    // камня или еды, по центру сверху появляется заметная кнопка. Нажали - режим включается. «✕» прячет её на 10 минут
    // (или пока не кончится другой ресурс).

    const float OfferSnooze = 600f;   // секунд: «✕» прячет кнопку
    static float offerHiddenUntil = -1f;
    static int offerHiddenMask;        // какие ресурсы были на нуле, когда кнопку спрятали
    static readonly string[] OfferRes = { "золото", "дерево", "камень", "еда" };

    /// <summary>Какие из золота, дерева, камня, еды сейчас на нуле (биты 0..3). 0 - всё есть.</summary>
    static int EmptyMask()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return 0;
        int m = 0;
        if (rm.gold <= 0) m |= 1;
        if (rm.wood <= 0) m |= 2;
        if (rm.stone <= 0) m |= 4;
        if (rm.food <= 0) m |= 8;
        return m;
    }

    /// <summary>Показывать ли кнопку сейчас (режим выключен, город загружен, мирное время, какой-то ресурс на нуле).</summary>
    static bool OfferWanted(out int mask)
    {
        mask = 0;
        if (On || WarManager.IsWar || WarCityMode.Active || RaidManager.ViewingBattle) return false;
        var bp = BuildingPlacer.Instance;
        var pm = PopulationManager.Instance;
        if (bp == null || !bp.WorldReady || pm == null || pm.Hall == null) return false;
        mask = EmptyMask();
        if (mask == 0) return false;
        // Спрятали «✕»: ждём 10 минут, но если кончилось что-то ещё - показываем сразу
        if (Time.unscaledTime < offerHiddenUntil && (mask & ~offerHiddenMask) == 0) return false;
        return true;
    }

    /// <summary>Рисует кнопку по центру сверху на высоте y (WarManager.OnGUI). Возвращает нижний край (или y, если кнопки нет).</summary>
    public static float DrawOffer(float y)
    {
        if (!OfferWanted(out int mask)) return y;

        var names = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) names.Add(Loc.T(OfferRes[i]));
        string what = string.Join(", ", names);
        bool phone = UiKit.Phone;
        float h = phone ? 44f : 34f, closeW = phone ? 40f : 28f;
        float w = Mathf.Min(phone ? 460f : 420f, UiKit.W - 24f);
        Rect r = new Rect(Mathf.Round(UiKit.W / 2f - w / 2f), y, w, h);
        if (UiKit.UnderWindow(r)) return y;   // поверх открыто окно - не мешаем
        TouchControls.NoteBox(r);              // палец на кнопке не двигает карту

        // Мягкая пульсация рамки: кнопку видно сразу
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
        Color old = GUI.color;
        GUI.color = new Color(UiKit.Warn.r, UiKit.Warn.g, UiKit.Warn.b, 0.35f + 0.4f * pulse);
        GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Texture2D.whiteTexture);
        GUI.color = old;

        float cd = CooldownLeft;
        Rect main = new Rect(r.x, r.y, r.width - closeW - 3f, r.height);
        string label = cd > 0f
            ? Loc.F("Режим накопления: через {0} с", Mathf.CeilToInt(cd))
            : Loc.F("Кончилось: {0}. Включить режим накопления", what);
        string tip = "Пока режим включён, здания ничего не тратят, жители не едят, а добытчики продолжают работать (вполсилы). " +
                     "Ресурсы только копятся. Выключить можно в меню «Город».";
        GUIStyle st = new GUIStyle(UiKit.ButtonStyle(UiKit.Kind.Primary)) { fontSize = phone ? 15 : 13, fontStyle = FontStyle.Bold, wordWrap = true };
        bool en = GUI.enabled;
        GUI.enabled = en && cd <= 0f;
        bool click = GUI.Button(main, label, st);
        GUI.enabled = en;
        UiKit.Tip(main, tip);
        if (click && cd <= 0f) Toggle();

        if (UiKit.Button(new Rect(main.xMax + 3f, r.y, closeW, r.height), "✕", UiKit.Kind.Normal, "Спрятать на 10 минут"))
        {
            offerHiddenUntil = Time.unscaledTime + OfferSnooze;
            offerHiddenMask = mask;
        }
        return r.yMax + 8f;
    }

    static void Set(bool on)
    {
        On = on;
        onSince = Time.unscaledTime;
        onSecondsBase = 0f;
    }

    /// <summary>Загрузка города: режим был включён (seconds - сколько уже длился).</summary>
    public static void Restore(bool on, float seconds)
    {
        Set(on);
        if (on) onSecondsBase = Mathf.Max(0f, seconds);
    }

    /// <summary>Добытчик: производит дерево, камень, золото или еду сам (или собирает ягоды).</summary>
    public static bool IsProducer(BuildingInstance b)
    {
        if (b == null || b.type == null) return false;
        BuildingType t = b.type;
        return t.prodWood > 0 || t.prodStone > 0 || t.prodGold > 0 || t.prodFood > 0 || b.ProducesBerries;
    }

    /// <summary>Работает ли здание в режиме накопления (если режим выключен - работают все).</summary>
    public static bool Works(BuildingInstance b)
    {
        if (!On || b == null || b.type == null) return true;
        return IsProducer(b) || b.IsTownHall || b.IsOutpost || b.type.name == CourierHouse.TypeName;
    }

    /// <summary>Здание с рабочими, которое сейчас стоит из-за режима (рабочие дома).</summary>
    public static bool Paused(BuildingInstance b)
    {
        return On && b != null && b.workersNeeded > 0 && !Works(b);
    }

    /// <summary>Множитель выработки добытчика.</summary>
    public static float ProduceFactor => On ? ProduceMul : 1f;

    // ---------- Меню «Город» ----------

    public const float RowHeight = 50f;

    public static void DrawRow(Rect r)
    {
        float cd = CooldownLeft;
        string label = On ? "Режим накопления: ВКЛ" : "Режим накопления";
        string tip = On
            ? $"Работают только Лесопилка, Шахта, Ферма и сборщики ягод (на {ProduceMul * 100f:F0}%), курьеры носят добычу. " +
              "Остальные рабочие сидят дома, здания на паузе. Новые жители не приходят.\nНи одно здание ничего не тратит, жители не едят и сами не лечатся: всё только копится.\nНочью все рабочие тоже на местах.\nНажмите, чтобы вернуть всех на работу."
            : "Город копит ресурсы: работают только добытчики (вполсилы), рабочие остальных зданий идут домой, новые жители не приходят. Выключите — и рабочие вернутся.\n" +
              "Ни одно здание ничего не тратит, жители не едят и сами не лечатся: всё только копится.\nНочью все рабочие тоже на местах.\nПомогает, если кончилась еда или другой ресурс.";
        if (cd > 0f) tip += $"\nПереключить можно через {Mathf.CeilToInt(cd)} с.";
        if (UiKit.Button(new Rect(r.x, r.y, r.width, 24f), label, cd <= 0f, On ? UiKit.Kind.Primary : UiKit.Kind.Normal, tip)) Toggle();

        string note;
        if (On)
        {
            int min = Mathf.FloorToInt(OnSeconds / 60f);
            note = UiKit.Col(UiKit.WarnHex, $"идёт {min} мин · добыча {ProduceMul * 100f:F0}% · расходов нет · прироста жителей нет");
        }
        else note = UiKit.Col(UiKit.MutedHex, "работают только добытчики, остальные отдыхают дома");
        LocGUI.Label(new Rect(r.x, r.y + 26f, r.width, 20f), note, UiKit.Small);
    }
}
