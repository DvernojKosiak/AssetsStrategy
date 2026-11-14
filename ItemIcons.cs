using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Картинки материалов и всех изготавливаемых вещей (Resources/Art/i/*.png, 64×64).
///  - Of(...) - какой картинкой рисовать вещь, товар, материал, модуль, ловушку или заслон (имя файла без .png).
///  - Draw - иконка в прямоугольнике; tier - метка качества в углу (хорошее - зелёная, наилучшее - золотая).
///  - DrawGrid / GridHeight - плитки «иконка + сколько» для складов (Цитадель, Хранилище); имя - во всплывающей подсказке.
/// Бойцы (id «unit:Имя») рисуются человечком через UnitIcons.
/// Картинки нет - ничего не рисуется (меню работают и без них).
/// </summary>
public static class ItemIcons
{
    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    static readonly HashSet<string> missing = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cache.Clear(); missing.Clear(); }

    public static Texture2D Tex(string id)
    {
        if (string.IsNullOrEmpty(id) || !GameArt.Enabled) return null;
        if (cache.TryGetValue(id, out Texture2D t) && t != null) return t;
        if (missing.Contains(id)) return null;
        t = Resources.Load<Texture2D>("Art/i/" + id);
        if (t == null) { missing.Add(id); return null; }
        cache[id] = t;
        return t;
    }

    static readonly Color TierGood = new Color(0.55f, 0.9f, 0.45f);
    static readonly Color TierBest = new Color(1f, 0.8f, 0.25f);

    /// <summary>Нарисовать иконку. tier: -1 - без метки, 0 - среднее, 1 - хорошее, 2 - наилучшее.</summary>
    public static void Draw(Rect r, string id, int tier = -1)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        if (UnitIcons.IsId(id)) { UnitIcons.Draw(r, id, tier); return; }   // боец: человечек, а не картинка из Art/i
        Texture2D t = Tex(id);
        if (t == null) return;
        GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
        if (tier <= 0) return;
        float s = Mathf.Max(5f, r.width * 0.2f);
        Color old = GUI.color;
        GUI.color = new Color(0.1f, 0.08f, 0.06f, 0.9f);
        GUI.DrawTexture(new Rect(r.xMax - s - 1f, r.yMax - s - 1f, s + 2f, s + 2f), Texture2D.whiteTexture);
        GUI.color = tier >= 2 ? TierBest : TierGood;
        GUI.DrawTexture(new Rect(r.xMax - s, r.yMax - s, s, s), Texture2D.whiteTexture);
        GUI.color = old;
    }

    public static bool Has(string id) { return UnitIcons.IsId(id) ? UnitIcons.Has(id) : Tex(id) != null; }

    // ---------- Что чем рисовать ----------

    public static string Of(Weapon w)
    {
        if (w == null) return null;
        return w.kind == WeaponKind.Sword ? "sword" : (w.kind == WeaponKind.Bow ? "bow" : "shot");
    }

    public static string Of(Shield s) { return s == null ? null : (s.chaosMk > 0 ? "shield_chaos" : "shield"); }

    public static string Of(BodyGear g) { return g == null ? null : (g.IsArmor ? "armor" : "cloak"); }

    public static string Of(WeaponRecipe r)
    {
        if (r == null) return null;
        if (r.isBody) return r.bodyKind == BodyKind.Armor ? "armor" : "cloak";
        if (r.isShield) return "shield";
        return r.kind == WeaponKind.Sword ? "sword" : (r.kind == WeaponKind.Bow ? "bow" : "shot");
    }

    public static string Refined(RefinedKind k)
    {
        switch (k)
        {
            case RefinedKind.Planks: return "planks";
            case RefinedKind.StonePlates: return "plates";
            case RefinedKind.ReinforcedPlates: return "reinforced";
            case RefinedKind.TarredBeams: return "beams";
            case RefinedKind.Monolith: return "monolith";
            default: return "steel";
        }
    }

    public static string Good(GoodKind k)
    {
        switch (k)
        {
            case GoodKind.Gold: return "gold";
            case GoodKind.Wood: return "wood";
            case GoodKind.Stone: return "stone";
            case GoodKind.Food: return "food";
            case GoodKind.Iron: return "iron";
            case GoodKind.Oats: return "oats";
            case GoodKind.Berries: return "berries";
            case GoodKind.Herbs: return "herbs";
            case GoodKind.Potions: return "potion";
            case GoodKind.Parts: return "parts";
            case GoodKind.Planks: return "planks";
            case GoodKind.Plates: return "plates";
            case GoodKind.Steel: return "steel";
            case GoodKind.Reinforced: return "reinforced";
            case GoodKind.Resin: return "resin";
            case GoodKind.Tar: return "tar";
            case GoodKind.Rubber: return "rubber";
            case GoodKind.SlowPotions: return "slow_potion";
            case GoodKind.Beams: return "beams";
            case GoodKind.Monolith: return "monolith";
            case GoodKind.Leaves: return "leaves";
            default: return null;
        }
    }

    public static string Module(ModuleKind k)
    {
        switch (k)
        {
            case ModuleKind.SteamRepeller: return "steam";
            case ModuleKind.Drawbridge: return "drawbridge";
            case ModuleKind.CannonLift: return "lift";
            case ModuleKind.FireShells: return "fire_shells";
            case ModuleKind.FrostShells: return "frost_shells";
            case ModuleKind.ArcaneShells: return "arcane_shells";
            case ModuleKind.ApShot: return "ap_shot";
            case ModuleKind.ChaosCore: return "chaos_core";
            case ModuleKind.FireworkBow: return "firework";
            default: return null;
        }
    }

    public static string Of(TrapKit t)
    {
        if (t == null) return null;
        if (t.combat) return "trap_combat";
        string id = TrapIcon(t.type);
        return Has(id) ? id : "trap";
    }

    /// <summary>Основа ловушки (меню Инженерного двора, запас ловушек).</summary>
    public static string TrapIcon(TrapBase b)
    {
        switch (b)
        {
            case TrapBase.Jaw: return "trap_jaw";
            case TrapBase.Spikes: return "trap_spikes";
            case TrapBase.Net: return "trap_net";
            case TrapBase.Spring: return "trap_spring";
            case TrapBase.Salute: return "trap_salute";
            default: return "trap";
        }
    }

    /// <summary>Добавка к ловушке - картинкой того, из чего она сделана.</summary>
    public static string TrapModIcon(TrapMod m)
    {
        switch (m)
        {
            case global::TrapMod.Shock: return "rubber";
            case global::TrapMod.Resin: return "resin";
            case global::TrapMod.Steel: return "steel";
            case global::TrapMod.Tar: return "tar";
            case global::TrapMod.Slow: return "slow_potion";
            case global::TrapMod.Ratchet: return "parts";
            default: return null;
        }
    }

    public static string Post(PostKind k)
    {
        if (k == PostKind.Waypoint) return "heal_post";
        if (k == PostKind.FieldTower) return "field_tower";
        return "barricade";
    }

    public static string Of(MarketItem it)
    {
        if (it == null) return null;
        switch (it.kind)
        {
            case ItemKind.Weapon: return Of(it.weapon);
            case ItemKind.Shield: return Of(it.shield);
            case ItemKind.Body: return Of(it.body);
            case ItemKind.Module: return Module(it.kit.kind);
            case ItemKind.Trap: return Of(it.trap);
            case ItemKind.Barricade: return it.gear != null ? Post(it.gear.kind) : "barricade";
            case ItemKind.Soldier: return UnitIcons.Id(it.soldier != null ? it.soldier.unit : (it.agent != null ? it.agent.ExpUnit : null));
            case ItemKind.Squad: return UnitIcons.Id(it.LeadUnit);
            default: return null;
        }
    }

    /// <summary>Иконка по названию рецепта («Доски», «Сталь», «Смоляная пропитка»...). null - не узнали.</summary>
    public static string ByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string n = name.ToLowerInvariant();
        if (n.Contains("укреп")) return "reinforced";
        if (n.Contains("монолит")) return "monolith";
        if (n.Contains("брус")) return "beams";
        if (n.Contains("пропит")) return "tar";
        if (n.Contains("резин")) return "rubber";
        if (n.Contains("доск")) return "planks";
        if (n.Contains("плит")) return "plates";
        if (n.Contains("стал")) return "steel";
        if (n.Contains("детал")) return "parts";
        if (n.Contains("похлёб") || n.Contains("похлеб")) return "pottage";
        if (n.Contains("замедл")) return "slow_potion";
        if (n.Contains("зель") || n.Contains("отрав")) return "potion";
        if (n.Contains("брон")) return "armor";
        if (n.Contains("плащ")) return "cloak";
        if (n.Contains("щит")) return "shield";
        if (n.Contains("лук")) return "bow";
        if (n.Contains("меч")) return "sword";
        return null;
    }

    // ---------- Плитки склада ----------

    public struct Entry
    {
        public string icon;
        public string name;
        public int count;
        public int tier;     // -1 - без качества
        public string countText;   // вместо числа (например «12 порц.»), необязательно
        public string limitText;   // лимит хранения для подсказки («лимит 400»), необязательно
        public bool full;          // упёрлись в лимит - число красное

        public Entry(string icon, string name, int count, int tier = -1, string countText = null)
        {
            this.icon = icon; this.name = name; this.count = count; this.tier = tier; this.countText = countText;
            limitText = null; full = false;
        }
    }

    public const float TileW = 48f, TileH = 56f, TileGap = 4f;

    static int PerRow(float w) { return Mathf.Max(1, Mathf.FloorToInt((w + TileGap) / (TileW + TileGap))); }

    public static float GridHeight(int count, float w)
    {
        if (count <= 0) return 0f;
        int rows = (count + PerRow(w) - 1) / PerRow(w);
        return rows * TileH + (rows - 1) * TileGap;
    }

    static GUIStyle countStyle;

    /// <summary>Нарисовать плитки «иконка + сколько». Возвращает высоту.</summary>
    public static float DrawGrid(float x, float y, float w, List<Entry> list)
    {
        if (list == null || list.Count == 0) return 0f;
        if (countStyle == null || countStyle.normal.textColor != UiKit.Text)
            countStyle = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
        int per = PerRow(w);
        for (int i = 0; i < list.Count; i++)
        {
            Entry e = list[i];
            Rect tile = new Rect(x + (i % per) * (TileW + TileGap), y + (i / per) * (TileH + TileGap), TileW, TileH);
            if (Event.current.type == EventType.Repaint)
            {
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.06f);
                GUI.DrawTexture(tile, Texture2D.whiteTexture);
                GUI.color = old;
            }
            Rect ir = new Rect(tile.x + (TileW - 36f) * 0.5f, tile.y + 2f, 36f, 36f);
            if (Has(e.icon)) Draw(ir, e.icon, e.tier);
            else LocGUI.Label(ir, UiKit.Col(UiKit.MutedHex, "?"), countStyle);
            string countText = e.countText ?? e.count.ToString();
            LocGUI.Label(new Rect(tile.x - 4f, tile.y + 38f, TileW + 8f, 16f), e.full ? UiKit.Col(UiKit.BadHex, countText) : countText, countStyle);
            string tierText = e.tier >= 0 ? $" ({TownHallStorage.TierName((QualityTier)e.tier).ToLower()})" : "";
            string limit = string.IsNullOrEmpty(e.limitText) ? "" : $"\n{e.limitText}{(e.full ? " — места нет" : "")}";
            UiKit.Tip(tile, $"{e.name}{tierText}: {countText}{limit}" + (e.tier >= 0 ? "\n\n" + TownHallStorage.QualityHelp : ""));
        }
        return GridHeight(list.Count, w);
    }

    /// <summary>Иконка слева от строки: рисует и возвращает, насколько сдвинуть текст.</summary>
    public static float Lead(Rect row, string id, int tier = -1)
    {
        if (!Has(id)) return 0f;
        float s = Mathf.Min(row.height, 26f);
        Draw(new Rect(row.x, row.y + (row.height - s) * 0.5f, s, s), id, tier);
        return s + 4f;
    }
}
