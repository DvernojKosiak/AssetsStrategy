using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("Казна игрока")]
    public int gold = 150;
    public int wood = 100;
    public int stone = 50;
    public int food = 80;

    [Header("Не показывается сверху — смотреть в Цитадели")]
    public int iron = 0;     // железная руда: добывает шахта Ур.3, из неё в мастерской делают сталь
    public int oats = 0;     // овёс: даёт ферма Ур.3, из него в Конюшне выращивают лошадей, на Кухне - похлёбку
    public int berries = 0;  // ягоды: приносят собиратели (Станция собирателей), нужны Кухне
    public int herbs = 0;    // травы (путь Магии): со станций носят в Цитадель, берёт Зельеварня
    public int potions = 0;  // зелья отравления: из Зельеварни носят в Цитадель / Хранилище
    public int parts = 0;    // детали (путь Инженерии): отливает Литейная, тратит Инженерный двор
    public int leaves = 0;   // листва: даёт Лесопилка Ур.4. Маскировочные накидки и др.
    public int resin = 0;    // смола: даёт Лесопилка Ур.3. Пропитка (Мастерская), резина (Литейная), зелье замедления, Поджигатели
    public int tar = 0;      // смоляная пропитка: варит Мастерская Ур.2 из смолы и дерева
    public int rubber = 0;   // резина: варит Литейная (путь Инженерии) из смолы
    public int slowPotions = 0;  // зелья замедления: варит Зельеварня (Магия) из трав и смолы, ими смазывают луки

    [Header("Валюта рынка")]
    public int marks = 0;    // марки Торговой гильдии: только через рынок (продажи, заказы гильдии), см. TradeGuild

    [Header("Лимиты хранения (Цитадель + Хранилища, см. StorageLimits)")]
    public bool limitsEnabled = true;

    /// <summary>Сколько этого ресурса в казне.</summary>
    public int Get(StoreRes r) { return Field(r); }

    ref int Field(StoreRes r)
    {
        switch (r)
        {
            case StoreRes.Gold: return ref gold;
            case StoreRes.Wood: return ref wood;
            case StoreRes.Stone: return ref stone;
            case StoreRes.Food: return ref food;
            case StoreRes.Iron: return ref iron;
            case StoreRes.Oats: return ref oats;
            case StoreRes.Berries: return ref berries;
            case StoreRes.Herbs: return ref herbs;
            case StoreRes.Potions: return ref potions;
            case StoreRes.Parts: return ref parts;
            case StoreRes.Leaves: return ref leaves;
            case StoreRes.Resin: return ref resin;
            case StoreRes.Tar: return ref tar;
            case StoreRes.Rubber: return ref rubber;
            case StoreRes.SlowPotions: return ref slowPotions;
            default: return ref marks;
        }
    }

    /// <summary>
    /// Срезать всё, что выше лимита. Ресурсы прибавляют и напрямую (gold += ...), поэтому
    /// проверка идёт и после каждого Add, и раз в кадр (LateUpdate). silent - не писать «склад полон».
    /// Если лимит стал меньше (снесли Хранилище), то, что уже лежит, не пропадает - просто не растёт,
    /// пока не опустится ниже лимита. Город загрузился с запасом сверх лимита - лишнее срезается один раз.
    /// </summary>
    public void ClampToLimits(bool silent = false)
    {
        if (!StorageLimits.Active) { limitsPrimed = false; return; }
        for (int i = 0; i < StorageLimits.ResCount; i++)
        {
            StoreRes r = (StoreRes)i;
            if (!StorageLimits.Limited(r)) continue;
            ref int v = ref Field(r);
            int allowed = StorageLimits.Cap(r);
            if (limitsPrimed && lastSeen[i] > allowed) allowed = lastSeen[i];
            if (v > allowed)
            {
                if (!silent) StorageLimits.ReportLost(StorageLimits.Name(r).ToLower(), v - allowed);
                v = allowed;
            }
            lastSeen[i] = v;
        }
        limitsPrimed = true;
    }

    readonly int[] lastSeen = new int[StorageLimits.ResCount];
    bool limitsPrimed;

    private void LateUpdate()
    {
        ClampToLimits();
        StorageLimits.Flush();
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public bool HasEnough(int g, int w, int s, int f)
    {
        return gold >= g && wood >= w && stone >= s && food >= f;
    }

    // Трата или получение из окна (кнопка игрока) - эта минута не идёт в доход города (CityIncome)
    static void ByPlayer() { if (Event.current != null) CityIncome.Skip(); }

    public bool Spend(int g, int w, int s, int f)
    {
        if (!HasEnough(g, w, s, f)) return false;
        ByPlayer();
        gold -= g;
        wood -= w;
        stone -= s;
        food -= f;
        return true;
    }

    // ---------- Для тестов ----------
    // F9 - много всего: обычные ресурсы, доски/плиты/сталь всех качеств, полные ягодные кусты и кухни
    [Header("Тест: F9 — выдать ресурсы")]
    public int cheatAmount = 5000;
    public int cheatRefined = 200;

    private void Update()
    {
        if (GameAdmin.On && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) GiveTestResources();
    }

    bool cheating;

    public void GiveTestResources()
    {
        cheating = true;
        CityIncome.Skip();
        // С лимитами F9 заполняет казну и склад до лимита (лишнее молча срезается)
        Add(cheatAmount, cheatAmount, cheatAmount, cheatAmount, cheatAmount, cheatAmount, cheatAmount, cheatAmount);
        parts += cheatRefined;
        resin += cheatRefined;
        leaves += cheatRefined;
        tar += cheatRefined;
        rubber += cheatRefined;
        slowPotions += cheatRefined / 10;
        potions += cheatRefined / 10;
        marks += 500;

        var st = TownHallStorage.Instance;
        if (st != null)
        {
            st.cheatSilent = true;
            foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
            {
                st.AddPlanks(q, cheatRefined);
                st.AddStonePlates(q, cheatRefined);
                st.AddSteel(q, cheatRefined);
                st.Add(RefinedKind.ReinforcedPlates, q, cheatRefined / 4);
                st.Add(RefinedKind.TarredBeams, q, cheatRefined / 4);
                st.Add(RefinedKind.Monolith, q, cheatRefined / 10);
            }
        }
        if (st != null) st.cheatSilent = false;
        ClampToLimits(true);
        cheating = false;
        BerryField.RefillAll();
        KitchenController.CheatFillAll();
        string capNote = StorageLimits.Active ? " (не больше лимитов склада)" : "";
        WarManager.Notify($"[Тест]{capNote} Выдано: по {cheatAmount} золота, дерева, камня, еды, руды, овса, ягод и по {cheatRefined} досок, плит и стали каждого качества, {cheatRefined} деталей, смолы, пропитки и резины, по {cheatRefined / 10} зелий, 500 марок; кухни полны", true);
    }

    // ironOre, oatsAmount, berriesAmount, herbsAmount, potionsAmount - необязательные параметры, чтобы старые вызовы не менять
    public void Add(int g, int w, int s, int f, int ironOre = 0, int oatsAmount = 0, int berriesAmount = 0,
                    int herbsAmount = 0, int potionsAmount = 0, int resinAmount = 0)
    {
        ByPlayer();
        resin += resinAmount;
        gold += g;
        wood += w;
        stone += s;
        food += f;
        iron += ironOre;
        oats += oatsAmount;
        berries += berriesAmount;
        herbs += herbsAmount;
        potions += potionsAmount;
        ClampToLimits(cheating);
    }

    public void AddHerbs(int n) { herbs += n; ClampToLimits(cheating); }
    public void AddPotions(int n) { potions += n; ClampToLimits(cheating); }

    public void AddParts(int n) { if (n > 0) parts += n; ClampToLimits(cheating); }
    public void AddResin(int n) { if (n > 0) resin += n; ClampToLimits(cheating); }
    public void AddLeaves(int n) { if (n > 0) leaves += n; ClampToLimits(cheating); }
    public void AddTar(int n) { if (n > 0) tar += n; ClampToLimits(cheating); }
    public void AddRubber(int n) { if (n > 0) rubber += n; ClampToLimits(cheating); }
    public void AddSlowPotions(int n) { if (n > 0) slowPotions += n; ClampToLimits(cheating); }

    static bool Take(ref int field, int amount)
    {
        if (amount <= 0) return true;
        if (field < amount) return false;
        ByPlayer();
        field -= amount;
        return true;
    }

    public bool SpendResin(int amount) { return Take(ref resin, amount); }
    public bool SpendLeaves(int amount) { return Take(ref leaves, amount); }
    public bool SpendTar(int amount) { return Take(ref tar, amount); }
    public bool SpendRubber(int amount) { return Take(ref rubber, amount); }
    public bool SpendSlowPotions(int amount) { return Take(ref slowPotions, amount); }

    public void AddMarks(int n) { if (n > 0) marks += n; ClampToLimits(cheating); }

    public bool SpendMarks(int amount)
    {
        if (amount <= 0) return true;
        if (marks < amount) return false;
        marks -= amount;
        return true;
    }

    public bool SpendParts(int amount)
    {
        if (amount <= 0) return true;
        if (parts < amount) return false;
        parts -= amount;
        return true;
    }

    public bool SpendHerbs(int amount)
    {
        if (amount <= 0) return true;
        if (herbs < amount) return false;
        herbs -= amount;
        return true;
    }

    public bool SpendPotions(int amount)
    {
        if (amount <= 0) return true;
        if (potions < amount) return false;
        potions -= amount;
        return true;
    }

    public bool SpendIron(int amount)
    {
        if (amount <= 0) return true;
        if (iron < amount) return false;
        iron -= amount;
        return true;
    }

    public bool SpendOats(int amount)
    {
        if (amount <= 0) return true;
        if (oats < amount) return false;
        oats -= amount;
        return true;
    }

    public bool SpendBerries(int amount)
    {
        if (amount <= 0) return true;
        if (berries < amount) return false;
        berries -= amount;
        return true;
    }

    // ---------- Верхняя полоса (y 0..40: ниже, на y=45, кнопка «Постройки») ----------
    // Строка 1 - казна (рисует ResourceManager), строка 2 - жители (рисует PopulationManager).
    // Тёмная подложка общая для обеих строк, поэтому рисуется «позади» остальных окон (GUI.depth).
    internal const float HudH = 40f;

    /// <summary>Сколько ширины может занять полоса: до кнопок справа сверху («Клан», «Путь», «Город», ⚙), с зазором.</summary>
    internal static float HudMaxW => Mathf.Max(200f, TopButtonsLeft - 10f);

    /// <summary>Левый край ряда кнопок справа сверху: «Домой» (город на войне), «Клан», «Путь», «Город», ⚙.</summary>
    internal static float TopButtonsLeft => WarCityMode.Active ? ClanWindow.CityButtonLeft - 12f - 128f : ClanWindow.CityButtonLeft;

    /// <summary>Правый край подложки полосы в прошлый раз (баннер войны и уведомления ставятся так, чтобы на неё не лечь).</summary>
    internal static float HudRight { get; private set; }

    class HudItem
    {
        public string iconId, name, text;
        public Texture2D icon;
        public float width;
        public HudItem(string iconId, string name, string text) { this.iconId = iconId; this.name = name; this.text = text; }
    }
    readonly List<HudItem> hud = new List<HudItem>();

    /// <summary>final-13: крупные числа казны коротко - «12,5к», «1,14М» (точное число - в подсказке).
    /// Раньше «1138455» растягивало верхнюю полосу и читалось как таблица.</summary>
    public static string Short(int v)
    {
        if (v < 10000) return v.ToString();
        bool ru = Loc.Current == Loc.Lang.RU;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (v < 1000000) return (v / 1000f).ToString(v < 100000 ? "0.#" : "0", inv) + (ru ? "к" : "k");
        return (v / 1000000f).ToString(v < 10000000 ? "0.##" : "0.#", inv) + (ru ? "М" : "M");
    }

    static string Item(string name, int value, string hex)
    {
        string v = UiKit.B(UiKit.Col(hex, Short(value)));
        return name == "" ? v : UiKit.Col(UiKit.MutedHex, name) + " " + v;
    }

    // Со своим лимитом: «Золото 150/3000», упёрлись в лимит - число красное
    static string Item(string name, int value, string hex, StoreRes r, bool limits)
    {
        if (!limits) return Item(name, value, hex);
        int cap = StorageLimits.Cap(r);
        string v = value >= cap ? UiKit.Col(UiKit.BadHex, Short(value)) : UiKit.Col(hex, Short(value));
        string tail = UiKit.B(v) + UiKit.Col(UiKit.MutedHex, "/" + Short(cap));
        return name == "" ? tail : UiKit.Col(UiKit.MutedHex, name) + " " + tail;
    }

    private void OnGUI()
    {
        if (WarView.IsOpen) return;   // открыта карта клановой войны
        UiKit.Apply();
        GUI.depth = 10;   // подложка под всеми окнами

        // Казна: значок ресурса и число (название - в подсказке). Нет картинки - пишем название словом
        bool lim = StorageLimits.Active;
        hud.Clear();
        hud.Add(new HudItem("gold", "Золото", Item("", gold, UiKit.GoldHex, StoreRes.Gold, lim)));
        hud.Add(new HudItem("wood", "Дерево", Item("", wood, UiKit.WoodHex, StoreRes.Wood, lim)));
        hud.Add(new HudItem("stone", "Камень", Item("", stone, UiKit.StoneHex, StoreRes.Stone, lim)));
        hud.Add(new HudItem("food", "Еда", Item("", food, UiKit.FoodHex, StoreRes.Food, lim)));
        // Марки гильдии - когда есть Торговый дом (или уже были марки)
        if (TradeGuild.Instance != null || marks > 0) hud.Add(new HudItem("marks", "◈ Марки", Item("", marks, UiKit.AccentHex)));

        GUIStyle st = new GUIStyle(UiKit.Body) { wordWrap = false, alignment = TextAnchor.MiddleLeft };
        const float Icon = 18f, Gap = 18f;
        float w = 0f;
        foreach (var it in hud)
        {
            it.icon = ItemIcons.Tex(it.iconId);
            if (it.icon == null) it.text = UiKit.Col(UiKit.MutedHex, it.name) + " " + it.text;
            it.width = (it.icon != null ? Icon + 4f : 0f) + st.CalcSize(Loc.Content(it.text)).x;
            w += it.width + Gap;
        }
        w -= Gap;
        bool pop = PopulationManager.Instance != null;
        if (pop) w = Mathf.Max(w, PopulationManager.HudRowWidth);
        w += 24f;
        w = Mathf.Min(w, HudMaxW);   // длинная строка жителей не уходит под кнопки справа (её конец - в подсказке)
        HudRight = w;

        // Подложка: рамка уходит за край экрана, видны только нижний и правый края
        LocGUI.Box(new Rect(-6f, -6f, w + 6f, (pop ? HudH : 26f) + 6f), GUIContent.none);

        Rect row = new Rect(12f, 3f, w - 12f, 20f);
        float x = row.x;
        foreach (var it in hud)
        {
            Rect cell = new Rect(x, row.y, it.width, row.height);
            float tx = x;
            if (it.icon != null)
            {
                GUI.DrawTexture(new Rect(x, row.y + (row.height - Icon) * 0.5f, Icon, Icon), it.icon, ScaleMode.ScaleToFit, true);
                tx += Icon + 4f;
            }
            LocGUI.Label(new Rect(tx, row.y, cell.xMax - tx + 2f, row.height), it.text, st);
            UiKit.Tip(cell, it.name + ": " + UiKit.B(ExactOf(it.iconId)) + "\n" + UiKit.Col(UiKit.InfoHex, WhereToGet(it.iconId)) + "\n\n" + ExtrasTip());
            x += it.width + Gap;
        }
    }

    string ExactOf(string id)
    {
        switch (id)
        {
            case "gold": return gold.ToString("N0");
            case "wood": return wood.ToString("N0");
            case "stone": return stone.ToString("N0");
            case "food": return food.ToString("N0");
            case "marks": return marks.ToString("N0");
            default: return "";
        }
    }

    // Где взять ресурс (подсказка тестеров final-8: новичок не понимал, откуда что берётся)
    static string WhereToGet(string id)
    {
        switch (id)
        {
            case "gold": return "Где взять: Шахта (понемногу, вместе с камнем; больше — в золотой зоне), набеги на деревни, продажа на рынке Торгового дома.";
            case "wood": return "Где взять: Лесопилка. Ставьте у леса (зелёная зона, «Показать зоны ресурсов» в меню города) — там дерева больше.";
            case "stone": return "Где взять: Шахта. В серо-голубой каменной зоне камня больше. Ещё — набеги.";
            case "food": return "Где взять: Ферма (на плодородной почве и у реки больше), ягоды — Станция собирателей. Еду едят все жители: следите, чтобы её хватало.";
            case "marks": return "Где взять: продажи на рынке и заказы гильдии в Торговом доме, магазин марок.";
            default: return "";
        }
    }

    // Подсказка к казне: прочие запасы (они лежат в Цитадели)
    string ExtrasTip()
    {
        bool lim = StorageLimits.Active;
        var p = new System.Collections.Generic.List<string>();
        void Extra(string name, int v, StoreRes r) { if (v > 0) p.Add(lim ? $"{name} {v}/{StorageLimits.Cap(r)}" : $"{name} {v}"); }
        Extra("руда", iron, StoreRes.Iron);
        Extra("овёс", oats, StoreRes.Oats);
        Extra("ягоды", berries, StoreRes.Berries);
        Extra("травы", herbs, StoreRes.Herbs);
        Extra("зелья отравления", potions, StoreRes.Potions);
        Extra("зелья замедления", slowPotions, StoreRes.SlowPotions);
        Extra("детали", parts, StoreRes.Parts);
        Extra("смола", resin, StoreRes.Resin);
        Extra("листва", leaves, StoreRes.Leaves);
        Extra("пропитка", tar, StoreRes.Tar);
        Extra("резина", rubber, StoreRes.Rubber);
        string extra = p.Count > 0 ? "Ещё в запасе: " + string.Join(", ", p) + "." : "Других запасов пока нет.";
        string limits = lim ? $"\nЧерез дробь — лимит: его даёт Цитадель (Ур. {StorageLimits.HallLevel}), каждое Хранилище добавляет ещё. Сверх лимита ничего не копится." : "";
        return "Казна города. " + extra + limits + "\nПодробно — в Цитадели.";
    }
}
