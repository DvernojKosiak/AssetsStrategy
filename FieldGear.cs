using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Запас полевого снаряжения (заслоны, пункты, заслоны с шипами и на колёсах, полевые башни), изготовленного в Кузне мастера
/// (путь Военного дела). Лежит в Цитадели: оттуда берётся и в оборону дома, и в набег (окно «В бой!»).
/// У каждой вещи своя сила (растёт с уровнем Кузни) и свой остаток лечебного запаса:
/// пункт, который уже раздал часть здоровья, возвращается в запас таким же «потраченным».
/// Сам запас не восстанавливается: пополнить его можно в Кузне мастера за еду и золото (Refill).
/// </summary>
public static class FieldGear
{
    // Все виды PostKind (быстрый заслон не изготавливается - его запас всегда пуст, ставят прямо за дерево)
    public const int Kinds = 6;

    public class Item
    {
        public PostKind kind;
        public float strength = 1f;   // множитель прочности и лечебного запаса
        public float pool;            // сколько здоровья пункт ещё может раздать

        public Item(PostKind kind, float strength)
        {
            this.kind = kind;
            this.strength = Mathf.Max(0.1f, strength);
            pool = FieldPost.Specs[(int)kind].healPool * this.strength;
        }

        public float MaxPool { get { return FieldPost.Specs[(int)kind].healPool * strength; } }

        // Чем выше - тем лучше вещь (берём лучшую первой)
        public float Score { get { return strength + (MaxPool > 0f ? pool / MaxPool : 0f); } }
    }

    static readonly List<Item>[] stock = NewStock();

    /// <summary>Пустые списки на каждый вид (так же устроен набор похода в RaidManager).</summary>
    public static List<Item>[] NewStock()
    {
        var a = new List<Item>[Kinds];
        for (int i = 0; i < Kinds; i++) a[i] = new List<Item>();
        return a;
    }

    // Static не сбрасывается между запусками Play, если отключён Domain Reload
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { foreach (var l in stock) l.Clear(); }

    public static int Count(PostKind k) { return stock[(int)k].Count; }

    /// <summary>Всё, что лежит в запасе (для рынка игроков).</summary>
    public static List<Item> All()
    {
        var all = new List<Item>();
        foreach (var l in stock) all.AddRange(l);
        return all;
    }

    public static bool Has(Item it) { return it != null && stock[(int)it.kind].Contains(it); }

    /// <summary>Убрать именно эту вещь (её продают).</summary>
    public static bool Remove(Item it) { return it != null && stock[(int)it.kind].Remove(it); }

    public static void Add(Item it)
    {
        if (it != null) stock[(int)it.kind].Add(it);
    }

    // ---------- Пополнение лечебного запаса (кнопка в Кузне мастера) ----------

    public const float HpPerFood = 5f;   // 1 еда - 5 здоровья в запасе пункта
    public const float HpPerGold = 8f;   // 1 золото - 8 здоровья (бинты, мази)

    /// <summary>Сколько пунктов в запасе раздали часть лечения.</summary>
    public static int SpentHealPosts()
    {
        int n = 0;
        foreach (var l in stock)
            foreach (var it in l)
                if (it.MaxPool > 0f && it.pool < it.MaxPool - 0.5f) n++;
        return n;
    }

    /// <summary>Сколько лечения не хватает всем пунктам в запасе до полного.</summary>
    public static float MissingPool()
    {
        float s = 0f;
        foreach (var l in stock)
            foreach (var it in l)
                if (it.MaxPool > 0f) s += Mathf.Max(0f, it.MaxPool - it.pool);
        return s;
    }

    public static int RefillFood(float hp) { return Mathf.CeilToInt(hp / HpPerFood); }
    public static int RefillGold(float hp) { return Mathf.CeilToInt(hp / HpPerGold); }

    /// <summary>
    /// Пополнить лечебный запас пунктов в Цитадели: на сколько хватит еды и золота (сначала самые пустые).
    /// Возвращает, сколько здоровья добавлено.
    /// </summary>
    public static float Refill()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return 0f;
        float need = MissingPool();
        float afford = Mathf.Min(rm.food * HpPerFood, rm.gold * HpPerGold);
        float give = Mathf.Floor(Mathf.Min(need, afford));
        if (give < 1f) return 0f;
        int food = Mathf.Min(rm.food, RefillFood(give)), gold = Mathf.Min(rm.gold, RefillGold(give));
        if (!rm.Spend(gold, 0, 0, food)) return 0f;

        var items = new List<Item>();
        foreach (var l in stock)
            foreach (var it in l)
                if (it.MaxPool > 0f && it.pool < it.MaxPool) items.Add(it);
        items.Sort((a, b) => (a.pool / a.MaxPool).CompareTo(b.pool / b.MaxPool));
        float left = give;
        foreach (var it in items)
        {
            if (left <= 0f) break;
            float add = Mathf.Min(it.MaxPool - it.pool, left);
            it.pool += add;
            left -= add;
        }
        return give - left;
    }

    /// <summary>Взять лучшую вещь этого вида.</summary>
    public static bool Take(PostKind k, out Item item)
    {
        item = null;
        List<Item> l = stock[(int)k];
        int best = -1;
        float bestScore = float.MinValue;
        for (int i = 0; i < l.Count; i++)
            if (l[i].Score > bestScore) { bestScore = l[i].Score; best = i; }
        if (best < 0) return false;
        item = l[best];
        l.RemoveAt(best);
        return true;
    }
}
