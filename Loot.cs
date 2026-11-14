using UnityEngine;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Добыча, которую кто-то несёт на себе: обычные ресурсы + доски/плиты по качеству.
///  - Враг разрушил производство - забирает всё, что лежало на складе здания.
///  - Враг разрушил Цитадель - забирает половину казны (золото, дерево, камень, еда, железная руда, овёс, ягоды)
///    и 20% досок/плит/стали, выбранных случайно.
///  - Добычу делят между врагами, которые ломали здание (каждый несёт свою часть).
///  - Врага убили - его добыча переходит к убийце (солдату). Убила башня - сразу в казну.
///  - Солдат с добычей сдаёт её, когда заходит в здание (Цитадель, казарма, дом)
///    или доходит до двери Цитадели / казармы.
///  - Солдата с добычей убил враг - враг забирает её обратно.
///  - Враг ушёл за край карты - добыча потеряна.
///  - Обновление 23: сложные вещи из чужой деревни (трофеи, Spoils) лежат в items (отнятое у хозяина) и copies
///    (копии: у хозяина они остались). Места в мешке не занимают и переходят вместе с мешком. Сдали дома -
///    отнятое возвращается на склады, а копии просто исчезают (у хозяина всё и так есть).
/// </summary>
public class Loot
{
    // Сколько забирают при разрушении Цитадели
    public const float HallRawShare = 0.5f;        // золото, дерево, камень, еда - половина казны, вторая половина остаётся
    public const float HallRefinedShare = 0.2f;    // доски и плиты

    // Золото лёгкое: столько монет занимают в мешке одно место
    public const int GoldPerSlot = 4;

    public int gold, wood, stone, food, iron, oats, berries, herbs, potions, resin;  // iron - железная руда, oats - овёс, berries - ягоды, resin - смола
    public readonly int[] planks = new int[3];     // индекс = (int)QualityTier
    public readonly int[] plates = new int[3];
    public readonly int[] steel = new int[3];
    public readonly int[] reinforced = new int[3];   // укреплённые плиты
    public readonly SpoilBundle items = new SpoilBundle();    // трофеи: отнято у хозяина (Spoils)
    public readonly SpoilBundle copies = new SpoilBundle();   // трофеи: копии, у хозяина остались

    /// <summary>Все трофеи одним списком (отнятое + такие же, как у хозяина).</summary>
    public SpoilBundle Trophies { get { var b = items.Copy(); b.Add(copies); return b; } }
    public bool HasItems => !items.IsEmpty || !copies.IsEmpty;

    /// <summary>Добыча картинками (ItemIcons.DrawGrid): ресурсы, материалы по качеству и трофеи. Обновление final-1.</summary>
    public List<ItemIcons.Entry> IconEntries()
    {
        var list = new List<ItemIcons.Entry>();
        void Res(int n, GoodKind k, string name) { if (n > 0) list.Add(new ItemIcons.Entry(ItemIcons.Good(k), name, n)); }
        Res(gold, GoodKind.Gold, "Золото"); Res(wood, GoodKind.Wood, "Дерево"); Res(stone, GoodKind.Stone, "Камень");
        Res(food, GoodKind.Food, "Еда"); Res(iron, GoodKind.Iron, "Руда"); Res(oats, GoodKind.Oats, "Овёс");
        Res(berries, GoodKind.Berries, "Ягоды"); Res(herbs, GoodKind.Herbs, "Травы"); Res(potions, GoodKind.Potions, "Зелья");
        Res(resin, GoodKind.Resin, "Смола");
        void Mat(int[] a, RefinedKind k, string name)
        {
            for (int q = 0; q < 3; q++) if (a[q] > 0) list.Add(new ItemIcons.Entry(ItemIcons.Refined(k), name, a[q], q));
        }
        Mat(planks, RefinedKind.Planks, "Доски"); Mat(plates, RefinedKind.StonePlates, "Каменные плиты");
        Mat(steel, RefinedKind.Steel, "Сталь"); Mat(reinforced, RefinedKind.ReinforcedPlates, "Укреплённые плиты");
        Spoils.AddEntries(Trophies, list);
        return list;
    }

    int[] Arr(RefinedKind k)
    {
        switch (k)
        {
            case RefinedKind.Planks: return planks;
            case RefinedKind.StonePlates: return plates;
            case RefinedKind.ReinforcedPlates: return reinforced;
            default: return steel;
        }
    }

    public int RefinedTotal
    {
        get
        {
            int n = 0;
            for (int i = 0; i < 3; i++) n += planks[i] + plates[i] + steel[i] + reinforced[i];
            return n;
        }
    }

    public int Total => gold + wood + stone + food + iron + oats + berries + herbs + potions + resin + RefinedTotal;
    public bool IsEmpty => Total <= 0 && !HasItems;

    // Сколько места занимает в мешке (золото - GoldPerSlot монет на место)
    public int Weight => Total - gold + GoldSlots(gold);
    public static int GoldSlots(int g) { return g <= 0 ? 0 : (g + GoldPerSlot - 1) / GoldPerSlot; }

    // Ценное (золото, доски, плиты, сталь, зелья) - ради него выкидывают дешёвое
    public int ValuableWeight => GoldSlots(gold) + RefinedTotal + potions;

    /// <summary>
    /// Мешок полон, а в src лежит ценное: выложить из bag в src дешёвое (камень, дерево, еду, овёс...),
    /// чтобы освободить до need мест, но не больше, чем ценного в src. Возвращает, сколько мест освободили.
    /// </summary>
    public static int MakeRoom(Loot bag, Loot src, int need)
    {
        if (bag == null || src == null || need <= 0) return 0;
        int want = Mathf.Min(need, src.ValuableWeight);
        int freed = 0;
        void Drop(ref int a, ref int b)
        {
            int n = Mathf.Min(a, want - freed);
            if (n <= 0) return;
            a -= n; b += n; freed += n;
        }
        Drop(ref bag.stone, ref src.stone);
        Drop(ref bag.wood, ref src.wood);
        Drop(ref bag.berries, ref src.berries);
        Drop(ref bag.oats, ref src.oats);
        Drop(ref bag.food, ref src.food);
        Drop(ref bag.herbs, ref src.herbs);
        Drop(ref bag.resin, ref src.resin);
        Drop(ref bag.iron, ref src.iron);
        return freed;
    }

    public void Clear()
    {
        gold = wood = stone = food = iron = oats = berries = herbs = potions = resin = 0;
        for (int i = 0; i < 3; i++) { planks[i] = 0; plates[i] = 0; steel[i] = 0; reinforced[i] = 0; }
        items.Clear();
        copies.Clear();
    }

    public void Add(Loot o)
    {
        if (o == null) return;
        gold += o.gold; wood += o.wood; stone += o.stone; food += o.food; iron += o.iron; oats += o.oats; berries += o.berries; herbs += o.herbs; potions += o.potions; resin += o.resin;
        for (int i = 0; i < 3; i++) { planks[i] += o.planks[i]; plates[i] += o.plates[i]; steel[i] += o.steel[i]; reinforced[i] += o.reinforced[i]; }
        items.Add(o.items);
        copies.Add(o.copies);
    }

    // Переложить всё содержимое в другой мешок (этот становится пустым)
    public void MoveTo(Loot dst)
    {
        if (dst == null || dst == this) return;
        dst.Add(this);
        Clear();
    }

    // Сдать в казну и на склад Цитадели
    public void DepositToTown()
    {
        if (IsEmpty) return;
        ReturnItems();
        CityIncome.Skip();   // добыча набега - не доход города
        var rm = ResourceManager.Instance;
        if (rm != null) rm.Add(gold, wood, stone, food, iron, oats, berries, herbs, potions, resin);

        var st = TownHallStorage.Instance;
        if (st != null)
        {
            for (int i = 0; i < 3; i++)
            {
                if (planks[i] > 0) st.AddPlanks((QualityTier)i, planks[i]);
                if (plates[i] > 0) st.AddStonePlates((QualityTier)i, plates[i]);
                if (steel[i] > 0) st.AddSteel((QualityTier)i, steel[i]);
                if (reinforced[i] > 0) st.Add(RefinedKind.ReinforcedPlates, (QualityTier)i, reinforced[i]);
            }
        }
        Clear();
    }

    /// <summary>
    /// Отнятые вещи вернулись домой (врага убили, бросил): на склады, что не влезло - в сундук трофеев.
    /// Копии исчезают: оригиналы и так лежат на месте.
    /// </summary>
    public void ReturnItems()
    {
        if (!items.IsEmpty)
        {
            var rest = new SpoilBundle();
            for (int i = 0; i < items.Kinds; i++)
            {
                int n = items.CountAt(i);
                int put = Mathf.Clamp(Spoils.Store(items.KeyAt(i), n), 0, n);
                if (put < n) rest.Add(items.KeyAt(i), n - put);
            }
            SpoilsChest.Put(rest);
        }
        items.Clear();
        copies.Clear();
    }

    // ---------- Разграбление ----------

    // Всё, что лежит на складе здания (шахта, лесопилка, ферма, станция собирателей...)
    public static Loot FromBuildingStorage(BuildingInstance b)
    {
        Loot l = new Loot();
        if (b == null) return l;
        l.gold = b.storedGold;
        l.wood = b.storedWood;
        l.stone = b.storedStone;
        l.food = b.storedFood;
        l.iron = b.storedIron;
        l.oats = b.storedOats;
        l.berries = b.storedBerries;
        l.herbs = b.storedHerbs;
        l.resin = b.storedResin;
        return l;
    }

    // Разграбление Цитадели: половина казны + 20% досок/плит в случайном порядке. Списывается сразу.
    // withItems = false (обновление 23, набег другого игрока): только простые ресурсы - зелья, доски, плиты
    // и прочие сложные вещи разыгрываются отдельно (Spoils), с защищённой половиной.
    public static Loot RaidTreasury(bool withItems = true)
    {
        Loot l = new Loot();

        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            l.gold = Mathf.FloorToInt(Mathf.Max(0, rm.gold) * HallRawShare);
            l.wood = Mathf.FloorToInt(Mathf.Max(0, rm.wood) * HallRawShare);
            l.stone = Mathf.FloorToInt(Mathf.Max(0, rm.stone) * HallRawShare);
            l.food = Mathf.FloorToInt(Mathf.Max(0, rm.food) * HallRawShare);
            l.iron = Mathf.FloorToInt(Mathf.Max(0, rm.iron) * HallRawShare);
            l.oats = Mathf.FloorToInt(Mathf.Max(0, rm.oats) * HallRawShare);
            l.berries = Mathf.FloorToInt(Mathf.Max(0, rm.berries) * HallRawShare);
            l.herbs = Mathf.FloorToInt(Mathf.Max(0, rm.herbs) * HallRawShare);
            l.potions = withItems ? Mathf.FloorToInt(Mathf.Max(0, rm.potions) * HallRawShare) : 0;
            l.resin = Mathf.FloorToInt(Mathf.Max(0, rm.resin) * HallRawShare);
            rm.Add(-l.gold, -l.wood, -l.stone, -l.food, -l.iron, -l.oats, -l.berries, -l.herbs, -l.potions, -l.resin);
        }

        var st = TownHallStorage.Instance;
        if (st == null || !withItems) return l;

        RefinedKind[] kinds = { RefinedKind.Planks, RefinedKind.StonePlates, RefinedKind.Steel, RefinedKind.ReinforcedPlates };

        // Сколько штук на складе всего (доски + плиты + сталь всех качеств)
        int total = 0;
        foreach (RefinedKind kind in kinds)
            foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
                total += st.Get(kind, q);
        if (total <= 0) return l;

        // 20%: целая часть + шанс на ещё одну штуку (чтобы и с маленького склада иногда что-то уносили)
        float exact = total * HallRefinedShare;
        int take = Mathf.FloorToInt(exact);
        if (Random.value < exact - take) take++;

        // Тянем по одной штуке случайно: чего больше на складе - то чаще и уносят
        for (int n = 0; n < take && total > 0; n++)
        {
            int r = Random.Range(0, total);
            bool picked = false;
            foreach (RefinedKind kind in kinds)
            {
                foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
                {
                    int have = st.Get(kind, q);
                    if (r >= have) { r -= have; continue; }
                    if (st.TrySpend(kind, q, 1)) l.Arr(kind)[(int)q]++;
                    picked = true;
                    break;
                }
                if (picked) break;
            }
            total--;
        }
        return l;
    }

    // ---------- Дележ между несколькими врагами ----------

    // Разделить на parts частей (по возможности поровну, остатки - случайным)
    public List<Loot> Split(int parts)
    {
        var res = new List<Loot>();
        parts = Mathf.Max(1, parts);
        for (int i = 0; i < parts; i++) res.Add(new Loot());

        int start = Random.Range(0, parts);
        int Share(int v, int i) => v / parts + (((i - start + parts) % parts) < v % parts ? 1 : 0);

        for (int i = 0; i < parts; i++)
        {
            Loot p = res[i];
            p.gold = Share(gold, i);
            p.wood = Share(wood, i);
            p.stone = Share(stone, i);
            p.food = Share(food, i);
            p.iron = Share(iron, i);
            p.oats = Share(oats, i);
            p.berries = Share(berries, i);
            p.herbs = Share(herbs, i);
            p.potions = Share(potions, i);
            p.resin = Share(resin, i);
        }

        // Доски и плиты раздаём поштучно случайным врагам
        for (int t = 0; t < 3; t++)
        {
            for (int k = 0; k < planks[t]; k++) res[Random.Range(0, parts)].planks[t]++;
            for (int k = 0; k < plates[t]; k++) res[Random.Range(0, parts)].plates[t]++;
            for (int k = 0; k < steel[t]; k++) res[Random.Range(0, parts)].steel[t]++;
            for (int k = 0; k < reinforced[t]; k++) res[Random.Range(0, parts)].reinforced[t]++;
        }

        // Трофеи - тоже поштучно
        var ib = new List<SpoilBundle>();
        var cb = new List<SpoilBundle>();
        foreach (var p in res) { ib.Add(p.items); cb.Add(p.copies); }
        items.SplitInto(ib);
        copies.SplitInto(cb);
        return res;
    }

    // ---------- Текст ----------

    public string ShortText
    {
        get
        {
            if (IsEmpty) return "ничего";
            var parts = new List<string>();
            if (gold > 0) parts.Add($"{gold} зол.");
            if (wood > 0) parts.Add($"{wood} дер.");
            if (stone > 0) parts.Add($"{stone} камня");
            if (food > 0) parts.Add($"{food} еды");
            if (iron > 0) parts.Add($"{iron} руды");
            if (oats > 0) parts.Add($"{oats} овса");
            if (berries > 0) parts.Add($"{berries} ягод");
            if (herbs > 0) parts.Add($"{herbs} трав");
            if (potions > 0) parts.Add($"{potions} зелий");
            if (resin > 0) parts.Add($"{resin} смолы");
            int pl = planks[0] + planks[1] + planks[2];
            int st = plates[0] + plates[1] + plates[2];
            int sl = steel[0] + steel[1] + steel[2];
            if (pl > 0) parts.Add($"{pl} досок");
            if (st > 0) parts.Add($"{st} плит");
            if (sl > 0) parts.Add($"{sl} стали");
            int rp = reinforced[0] + reinforced[1] + reinforced[2];
            if (rp > 0) parts.Add($"{rp} укр. плит");
            // Трофеи одним списком: игроку всё равно, отнято это или такое же (обновление final-1)
            SpoilBundle trophies = Trophies;
            if (!trophies.IsEmpty) parts.Add($"трофеи: {trophies.Text}");
            return string.Join(", ", parts);
        }
    }
}
